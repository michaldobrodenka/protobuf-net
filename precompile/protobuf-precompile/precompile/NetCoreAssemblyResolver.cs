using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.DependencyModel.Resolution;

namespace ProtoBuf.Precompile
{
	public sealed class NetCoreAssemblyResolver : ICustomAssemblyResolver
	{
		private readonly ICompilationAssemblyResolver _assemblyResolver;
		private readonly DependencyContext _dependencyContext;
		private readonly AssemblyLoadContext _loadContext;
		// private readonly List<Assembly> resolvedAssemblies = new List<Assembly>();

		public NetCoreAssemblyResolver(string assemblyPath)
		{
			//this assemblyPath has to have a deps.json-file in the same directory.
			//InitialAssembly = Assembly.LoadFile(assemblyPath);
			var initialAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);

			_dependencyContext = DependencyContext.Load(initialAssembly);

			var nugetPackageDirectory = GetNugetRootDirectory();
			Console.WriteLine("Nuget root: " + nugetPackageDirectory);
			var packageCompilationAssemblyResolver = new PackageCompilationAssemblyResolver(nugetPackageDirectory);

			this._assemblyResolver = new CompositeCompilationAssemblyResolver
				(new ICompilationAssemblyResolver[]
				{
					new AppBaseCompilationAssemblyResolver(Path.GetDirectoryName(assemblyPath)),
					new ReferenceAssemblyPathResolver(),
					packageCompilationAssemblyResolver
				});

			this._loadContext = AssemblyLoadContext.GetLoadContext(initialAssembly);
			if (this._loadContext == null)
				throw new Exception("_loadContext null!");

			this._loadContext.Resolving += OnResolving;

			// only in net core 3.1
			//resolvedAssemblies.AddRange(this._loadContext.Assemblies);
		}


		private static string GetNugetRootDirectory()
		{
			var userHome = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Environment.GetEnvironmentVariable("USERPROFILE") : Environment.GetEnvironmentVariable("HOME");

			return string.IsNullOrEmpty(userHome) ? null : Path.Combine(userHome, ".nuget", "packages");
		}


		public string TryResolve(string name)
		{
			var protobufNet = "protobuf-net";

			// do not resolve the protobuf IKVM binary, redirect to protobuf
			if (name.StartsWith(protobufNet, StringComparison.InvariantCultureIgnoreCase))
				name = protobufNet;

			// these were resolved on load
			//var nativeAsm = resolvedAssemblies.FirstOrDefault(k => string.Equals(name, Path.GetFileNameWithoutExtension(k.Location), StringComparison.InvariantCultureIgnoreCase));
			//if (nativeAsm != null)
			//	return nativeAsm.Location;

			// try loading it manually. from nuget cache most likely
			var runtimeLibrary = _dependencyContext.RuntimeLibraries.FirstOrDefault(k => string.Equals(name, k.Name, StringComparison.InvariantCultureIgnoreCase));
			if (runtimeLibrary != null)
			{
				var compilationLibrary = RuntimeLibraryToCompilationLibrary(runtimeLibrary);

				var libpaths = new List<string>();
				if (_assemblyResolver.TryResolveAssemblyPaths(compilationLibrary, libpaths) && libpaths.Count > 0)
				{
					// it works but i am not completely sure if it is right. (if package name <> dll name, or there are other dlls supplied)

					var path = libpaths[0];
					path = Path.GetFullPath(path); // the path can have mismatched slashes, this fixes it
					return path;
				}
			}

			return null;
		}


		private Assembly OnResolving(AssemblyLoadContext context, AssemblyName name)
		{
			bool NamesMatch(RuntimeLibrary runtime)
			{
				return string.Equals(runtime.Name, name.Name, StringComparison.OrdinalIgnoreCase);
			}

			var library = this._dependencyContext.RuntimeLibraries.FirstOrDefault(NamesMatch);
			if (library == null)
				return null;

			var wrapper = RuntimeLibraryToCompilationLibrary(library);

			var assemblies = new List<string>();
			this._assemblyResolver.TryResolveAssemblyPaths(wrapper, assemblies);
			if (assemblies.Count > 0)
			{
				return this._loadContext.LoadFromAssemblyPath(assemblies[0]);
			}

			return null;
		}


		private static CompilationLibrary RuntimeLibraryToCompilationLibrary(RuntimeLibrary library)
		{
			return new CompilationLibrary(library.Type, library.Name, library.Version, library.Hash, library.RuntimeAssemblyGroups.SelectMany(g => g.AssetPaths), library.Dependencies, library.Serviceable, library.Path, library.HashPath);
		}


		public void Dispose()
		{
			_loadContext.Resolving -= this.OnResolving;
		}
	}
}
