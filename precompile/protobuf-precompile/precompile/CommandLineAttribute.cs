using System;
using System.Collections.Generic;
using System.Reflection;

namespace ProtoBuf.Precompile
{
	/// <summary>
	/// Defines a mapping from command-line attributes to properties
	/// </summary>
	[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
	public class CommandLineAttribute : Attribute
	{
		private readonly string prefix;


		/// <summary>
		/// Create a new CommandLineAttribute object for the given prefix
		/// </summary>
		public CommandLineAttribute(string prefix)
		{
			this.prefix = prefix;
		}


		/// <summary>
		/// The prefix to recognise this command-line switch
		/// </summary>
		public string Prefix
		{
			get
			{
				return prefix;
			}
		}


		/// <summary>
		/// Attempt to parse the incoming command-line switches, matching by prefix
		/// onto properties of the specified type
		/// </summary>
		public static bool TryParse<T>(string[] args, out T result) where T : class, new()
		{
			result = new T();
			var allGood = true;
			var props = typeof(T).GetProperties();

			char[] leadChars =
			{
				'/', '+', '-'
			};

			foreach (var t in args)
			{
				string arg = t.Trim(), prefix, value;
				if (arg.IndexOfAny(leadChars) == 0)
				{
					var idx = arg.IndexOf(':');
					if (idx < 0)
					{
						prefix = arg.Substring(1);
						value = "";
					}
					else
					{
						prefix = arg.Substring(1, idx - 1);
						value = arg.Substring(idx + 1);
					}
				}
				else
				{
					prefix = "";
					value = arg;
				}

				PropertyInfo foundProp = null;

				foreach (var prop in props)
				{
					foreach (CommandLineAttribute atttib in prop.GetCustomAttributes(typeof(CommandLineAttribute), true))
					{
						if (atttib.Prefix == prefix)
						{
							foundProp = prop;
							break;
						}
					}

					if (foundProp != null) break;
				}

				if (foundProp == null)
				{
					allGood = false;
					MsBuildLog.LogError("PA0001", $"Argument not understood: {arg}");
				}
				else
				{
					if (foundProp.PropertyType == typeof(string))
					{
						foundProp.SetValue(result, value, null);
					}
					else if (foundProp.PropertyType == typeof(List<string>))
					{
						((List<string>) foundProp.GetValue(result, null)).Add(value);
					}
					else if (foundProp.PropertyType == typeof(bool))
					{
						foundProp.SetValue(result, true, null);
					}
					else if (foundProp.PropertyType.IsEnum)
					{
						object parsedValue;
						try
						{
							parsedValue = Enum.Parse(foundProp.PropertyType, value, true);
						}
						catch
						{
							MsBuildLog.LogError("PA0002", $"Invalid option for: {arg} - Options: {string.Join(", ", Enum.GetNames(foundProp.PropertyType))}");
							allGood = false;
							parsedValue = null;
						}

						if (parsedValue != null) foundProp.SetValue(result, parsedValue, null);
					}
				}
			}

			return allGood;
		}
	}
}
