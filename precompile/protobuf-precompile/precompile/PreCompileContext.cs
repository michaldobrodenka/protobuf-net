using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using IKVM.Reflection;
using ProtoBuf.Meta;
using ResolveEventArgs = IKVM.Reflection.ResolveEventArgs;
using Type = IKVM.Reflection.Type;

namespace ProtoBuf.Precompile
{
	/// <summary>
	/// Defines the rules for a precompilation operation
	/// </summary>
	public class PreCompileContext
	{
		/// <summary>
		/// Create a new instance of PreCompileContext
		/// </summary>
		public PreCompileContext()
		{
			Accessibility = RuntimeTypeModel.Accessibility.Public;
		}


		/// <summary>
		/// The target framework to use. e.g. ".NETStandard\v2.0"
		/// </summary>
		[CommandLine("f"), CommandLine("framework")]
		public string Framework { get; set; }

		/// <summary>
		/// Locations to check for referenced assemblies
		/// </summary>
		[CommandLine("p"), CommandLine("probe")]
		public List<string> ProbePaths { get; } = new List<string>();

		/// <summary>
		/// The paths for assemblies to process
		/// </summary>
		[CommandLine("i")]
		public List<string> Inputs { get; } = new List<string>();

		/// <summary>
		/// The type name of the serializer to generate
		/// </summary>
		[CommandLine("t"), CommandLine("type")]
		public string TypeName { get; set; }

		/// <summary>
		/// The name of the assembly to generate
		/// </summary>
		[CommandLine("o"), CommandLine("out")]
		public string OutputAssemblyPath { get; set; }

		/// <summary>
		/// Show help
		/// </summary>
		[CommandLine("?"), CommandLine("help"), CommandLine("h")]
		public bool Help { get; set; }

		/// <summary>
		/// The accessibility of the generated type
		/// </summary>
		[CommandLine("access")]
		public RuntimeTypeModel.Accessibility Accessibility { get; set; }

		/// <summary>
		/// The path to the file to use to sign the assembly
		/// </summary>
		[CommandLine("keyfile")]
		public string KeyFile { get; set; }

		/// <summary>
		/// The container to use to sign the assembly
		/// </summary>
		[CommandLine("keycontainer")]
		public string KeyContainer { get; set; }

		/// <summary>
		/// The public key (in hexadecimal) to use to sign the assembly
		/// </summary>
		[CommandLine("publickey")]
		public string PublicKey { get; set; }

		private readonly List<ICustomAssemblyResolver> resolvers = new List<ICustomAssemblyResolver>();

		static string TryInferFramework(string path)
		{
			string imageRuntimeVersion = null;
			try
			{
				using (var uni = new Universe())
				{
					uni.AssemblyResolve += (s, a) => ((Universe)s).CreateMissingAssembly(a.Name);
					var asm = uni.LoadFile(path);
					imageRuntimeVersion = asm.ImageRuntimeVersion;

					var attr = uni.GetType("System.Attribute, mscorlib");

					foreach (var attrib in asm.__GetCustomAttributes(attr, false))
					{
						if (attrib.Constructor.DeclaringType.FullName == "System.Runtime.Versioning.TargetFrameworkAttribute"
							&& attrib.ConstructorArguments.Count == 1)
						{
							var parts = ((string)attrib.ConstructorArguments[0].Value).Split(',');
							string runtime = null, version = null, profile = null;

							foreach (var t in parts)
							{
								var idx = t.IndexOf('=');
								if (idx < 0)
								{
									runtime = t;
								}
								else
								{
									switch (t.Substring(0, idx))
									{
										case "Version":
											version = t.Substring(idx + 1);
											break;
										case "Profile":
											profile = t.Substring(idx + 1);
											break;
									}
								}
							}

							if (runtime != null)
							{
								var sb = new StringBuilder(runtime);
								if (version != null)
								{
									sb.Append(Path.DirectorySeparatorChar).Append(version);
								}

								if (profile != null)
								{
									sb.Append(Path.DirectorySeparatorChar).Append("Profile").Append(Path.DirectorySeparatorChar).Append(profile);
								}

								var targetFramework = sb.ToString();
								return targetFramework;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				// not really fussed; we could have multiple inputs to try, and the user
				// can always use -f:blah to specify it explicitly
				Debug.WriteLine(ex.Message);
			}

			if (!string.IsNullOrEmpty(imageRuntimeVersion))
			{
				var frameworkPath = Path.Combine(
												 Environment.ExpandEnvironmentVariables(@"%windir%\Microsoft.NET\Framework"),
												 imageRuntimeVersion);
				if (Directory.Exists(frameworkPath)) return frameworkPath;
			}

			return null;
		}


		/// <summary>
		/// Check the context for obvious errrs
		/// </summary>
		public bool SanityCheck()
		{
			var allGood = true;

			if (Inputs.Count == 0)
			{
				MsBuildLog.LogError("SC0001", "No input assemblies");
				allGood = false;
			}

			for (var i = 0; i < Inputs.Count; i++)
			{
				var fullPath = FixPath(Inputs[i]);

				if (!File.Exists(fullPath))
				{
					MsBuildLog.LogError("SC0001", $"Input assembly not found: {fullPath}");
					allGood = false;
				}

				Inputs[i] = fullPath;
			}

			if (string.IsNullOrEmpty(TypeName))
			{
				MsBuildLog.LogError("SC0002", "No serializer type-name specified");
				allGood = false;
			}

			if (string.IsNullOrEmpty(OutputAssemblyPath))
			{
				MsBuildLog.LogError("SC0003", "No output assembly file specified");
				allGood = false;
			}
			else
			{
				OutputAssemblyPath = FixPath(OutputAssemblyPath);
			}

			if (string.IsNullOrEmpty(Framework))
			{
				foreach (var inp in Inputs)
				{
					var tmp = TryInferFramework(inp);
					if (tmp != null)
					{
						Console.WriteLine($"Detected framework: {tmp}");
						Framework = tmp;
						break;
					}
				}
			}
			else
			{
				Console.WriteLine($"Configured framework: {Framework}");
			}

			if (string.IsNullOrEmpty(Framework))
			{
				Console.WriteLine($"No framework specified; defaulting to {Environment.Version}");
				ProbePaths.Add(Path.GetDirectoryName(typeof(string).Assembly.Location));
			}
			else
			{
				if (Directory.Exists(Framework))
				{
					// very clear and explicit
					ProbePaths.Add(Framework);
				}
				else if (Framework.IndexOf("standard", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					// on net core/standard, let him find it automatically the netcore binaries
					// they are 'somewhere', elsewhere than .net
					ProbePaths.Add(Path.GetDirectoryName(typeof(string).Assembly.Location));
				}
				else
				{
					var root = Environment.GetEnvironmentVariable("ProgramFiles(x86)");

					if (string.IsNullOrEmpty(root))
						root = Environment.GetEnvironmentVariable("ProgramFiles");

					if (string.IsNullOrEmpty(root))
					{
						ProbePaths.Add(RuntimeEnvironment.GetRuntimeDirectory());
					}
					else
					{
						if (string.IsNullOrEmpty(root))
						{
							MsBuildLog.LogError("SC0011", "Framework reference assemblies root folder is empty");
							return false;
						}

						root = Path.Combine(root, @"Reference Assemblies\Microsoft\Framework\");
						if (!Directory.Exists(root))
						{
							MsBuildLog.LogError("SC0012", "Framework reference assemblies root folder could not be found");
							allGood = false;
						}
						else
						{
							var frameworkRoot = Path.Combine(root, Framework);
							if (Directory.Exists(frameworkRoot))
							{
								// fine
								ProbePaths.Add(frameworkRoot);
							}
							else
							{
								var err = $"Framework not found: {Framework}. Available frameworks are:";

								var files = Directory.GetFiles(root, "mscorlib.dll", SearchOption.AllDirectories);
								foreach (var file in files)
								{
									var dir = Path.GetDirectoryName(file);

									if (dir != null && dir.StartsWith(root))
										dir = dir.Substring(root.Length);

									err += $" \"{dir}\"";
								}

								MsBuildLog.LogError("SC0004", err);

								allGood = false;
							}
						}
					}
				}
			}

			if (!string.IsNullOrEmpty(KeyFile) && !File.Exists(KeyFile))
			{
				MsBuildLog.LogError("SC0006", $"Key file not found: {KeyFile}");
				allGood = false;
			}

			foreach (var inp in Inputs)
			{
				if (File.Exists(inp))
				{
					var dir = Path.GetDirectoryName(inp);
					if (!ProbePaths.Contains(dir)) ProbePaths.Add(dir);
				}
				else
				{
					MsBuildLog.LogError("SC0010", $"Input not found: {inp}");
					allGood = false;
				}
			}

			return allGood;
		}

		private static string FixPath(string path)
		{
			if (string.IsNullOrEmpty(path))
				return null;

			var otherSep = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? '/' : '\\';
			path = path.Replace(otherSep, Path.DirectorySeparatorChar);

			path = Path.GetFullPath(path);
			return path;
		}


		IEnumerable<string> ProbeForFiles(string file)
		{
			foreach (var probePath in ProbePaths)
			{
				var combined = Path.Combine(probePath, file);
				if (File.Exists(combined))
				{
					yield return combined;
				}
			}
		}


		/// <summary>
		/// Perform the precompilation operation
		/// </summary>
		public bool Execute()
		{
			// model to work with
			var model = TypeModel.Create();

			model.Universe.AssemblyResolve += (sender, args) => AssemblyResolve(args, model);
			var allGood = true;

			var nslib = ResolveNewAssembly(model.Universe, "netstandard.dll");
			// test netframework, ao refs
			if (nslib == null)
			{
				MsBuildLog.LogError("SC0007", "Framework not found!");
				allGood = false;
			}

			{
				var path = Path.Combine(Path.GetDirectoryName(GetType().Assembly.Location), "ns20", "netstandard.dll");
				var exp = model.Universe.LoadFileAlone(path);
				if (exp == null)
				{
					MsBuildLog.LogError("SC0007", "Export framework not found!");
					allGood = false;
				}
				else
				{
					model.Universe.ExportFramework = exp;
				}
			}

			if (!allGood)
				return false;

			var assemblies = new List<Assembly>();
			MetaType metaType = null;
			foreach (var file in Inputs)
			{
				var assembly = model.Load(file);
				assemblies.Add(assembly);

#if NETCOREAPP
				var resolver = new NetCoreAssemblyResolver(assembly.Location);
#else
				var resolver = new OldStyleAssemblyResolver();
#endif
				resolvers.Add(resolver);
			}

			// scan for obvious protobuf types
			var attributeType = model.Universe.GetType("System.Attribute, netstandard");
			var toAdd = new List<Type>();
			foreach (var asm in assemblies)
			{
				foreach (var type in asm.GetTypes())
				{
					var add = false;
					if (!(type.IsClass || type.IsValueType))
						continue;

					foreach (var attrib in type.__GetCustomAttributes(attributeType, true))
					{
						var name = attrib.Constructor.DeclaringType.FullName;
						switch (name)
						{
							case "ProtoBuf.ProtoContractAttribute":
								add = true;
								break;
						}

						if (add) break;
					}

					if (add) toAdd.Add(type);
				}
			}

			if (toAdd.Count == 0)
			{
				MsBuildLog.LogError("SC0009", "No [ProtoContract] types found; nothing to do!");
				return false;
			}

			// add everything we explicitly know about
			toAdd.Sort((x, y) => string.Compare(x.FullName, y.FullName, StringComparison.InvariantCultureIgnoreCase));
			Console.WriteLine("Adding explicit types...");
			foreach (var type in toAdd)
			{
				Console.WriteLine($" {type.FullName}");
				var tmp = model.Add(type, true);
				metaType = metaType ?? tmp;
			}

			// add everything else we can find
			model.Cascade();
			var inferred = new List<Type>();
			foreach (MetaType type in model.GetTypes())
			{
				if (!toAdd.Contains(type.Type))
					inferred.Add(type.Type);
			}

			inferred.Sort((x, y) => string.Compare(x.FullName, y.FullName, StringComparison.InvariantCultureIgnoreCase));
			Console.WriteLine("Adding inferred types...");
			foreach (var type in inferred)
			{
				Console.WriteLine($" {type.FullName}");
			}


			// configure the output file/serializer name, and borrow the framework particulars from
			// the type we loaded
			var options = new RuntimeTypeModel.CompilerOptions
			{
				TypeName = TypeName,
				OutputPath = OutputAssemblyPath,
				ImageRuntimeVersion = nslib.ImageRuntimeVersion,
				MetaDataVersion = 0x20000, // use .NET 2 onwards
				KeyContainer = KeyContainer,
				KeyFile = KeyFile,
				PublicKey = PublicKey
			};
			if (nslib.ImageRuntimeVersion == "v1.1.4322")
			{
				// .NET 1.1-style
				options.MetaDataVersion = 0x10000;
			}

			if (metaType != null)
			{
				var asm = options.SetFrameworkOptions(metaType);

				var wantedFramework = GetFrameworkType(Framework);
				var chosenFramework = GetFrameworkType(options.TargetFrameworkName);

				// NETStandard was required but source assembly is NETFramework or vice versa
				if (!string.Equals(wantedFramework, chosenFramework, StringComparison.OrdinalIgnoreCase))
				{
					MsBuildLog.LogWarning("SCW001", $"Warning: Framework mismatch. wanted:'{Framework}' chosen:'{options.TargetFrameworkName}' deciding assembly='{asm}'");
				}
			}

			options.Accessibility = this.Accessibility;
			Console.WriteLine($"Compiling {options.TypeName} to {options.OutputPath}...");
			// GO WORK YOUR MAGIC, CRAZY THING!!
			model.Compile(options);
			Console.WriteLine("All done");

			return true;
		}


		private static string GetFrameworkType(string s)
		{
			// It can me nultiple..
			// .NETStandard\v2.0
			// .NETFramework,Version=v4.6.1

			if (string.IsNullOrEmpty(s))
				return s;

			var elems = s.Split(new[]
			{
				',', '\\', '/'
			}, StringSplitOptions.RemoveEmptyEntries);

			if (elems.Length < 1)
				return s;

			return elems[0];
		}


		private Assembly AssemblyResolve(ResolveEventArgs args, RuntimeTypeModel model)
		{
			string nameOnly = args.Name.Split(',')[0];

			if (nameOnly == "IKVM.Reflection" && args.RequestingAssembly != null && args.RequestingAssembly.FullName.StartsWith("protobuf-net"))
			{
				throw new PreCompileException("This operation needs access to the protobuf-net.dll used by your library, **in addition to** the protobuf-net.dll that is included with the precompiler; the easiest way to do this is to ensure the referenced protobuf-net.dll is in the same folder as your library.");
			}

			//if (nameOnly.StartsWith("System.", StringComparison.InvariantCultureIgnoreCase))
			//	nameOnly = "Netstandard";

			var uni = model.Universe;
			foreach (var tmp in uni.GetAssemblies())
			{
				if (tmp.GetName().Name == nameOnly)
					return tmp;
			}

			var asm = ResolveNewAssembly(uni, $"{nameOnly}.dll");
			if (asm != null)
				return asm;

			asm = ResolveNewAssembly(uni, $"{nameOnly}.exe");
			if (asm != null)
				return asm;

			// last chance... look into nuget cache
			foreach (var resolver in resolvers)
			{
				var path = resolver.TryResolve(nameOnly);
				if (!string.IsNullOrEmpty(path))
				{
					asm = uni.LoadFile(path);
					if (asm != null)
					{
						Console.WriteLine($"Resolved package {nameOnly} -> {path}");
						return asm;
					}
				}
			}

			throw new PreCompileException($"All assemblies must be resolved explicity; did not resolve: {args.Name}");
		}


		private Assembly ResolveNewAssembly(Universe uni, string fileName)
		{
			foreach (var match in ProbeForFiles(fileName))
			{
				var asm = uni.LoadFile(match);
				if (asm != null)
				{
					Console.WriteLine($"Resolved {match}");
					return asm;
				}
			}

			return null;
		}


		/// <summary>
		/// Return the syntax guide for the utility
		/// </summary>
		public string GetUsage()
		{
			return
				@"Generates a serialization dll that can be used with just the
(platform-specific) protobuf-net core, allowing fast and efficient
serialization even on light frameworks (CF, SL, SP7, Metro, etc).

The input assembly(ies) is(are) anaylsed for types decorated with
[ProtoContract]. All such types are added to the model, as are any
types that they require.

Note: the compiler must be able to resolve a protobuf-net.dll
that is suitable for the target framework; this is done most simply
by ensuring that the appropriate protobuf-net.dll is next to the
input assembly.

Options:

    -f[ramework]:<framework>
           Can be an explicit path, or a path relative to:
           Reference Assemblies\Microsoft\Framework
    -o[ut]:<file>
           Output dll path
    -t[ype]:<typename>
           Type name of the serializer to generate
    -p[robe]:<path>
           Additional directory to probe for assemblies
    -access:<access>
           Specify accessibility of generated serializer
           to 'Public' or 'Internal'
    -keyfile:<file>
           Sign with the file (snk, etc) specified
    -keycontainer:<container>
           Sign with the container specified
    -publickey:<key>
           Sign with the public key specified (as hex)
    -i:<file>
           Input file to analyse

Example:

    precompile -f:.NETCore\v4.5 MyDtos\My.dll -o:MySerializer.dll
        -t:MySerializer";
		}
	}
}
