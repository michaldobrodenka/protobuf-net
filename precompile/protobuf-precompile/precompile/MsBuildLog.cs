using System;
using System.Reflection;

namespace ProtoBuf.Precompile
{
	/// <summary>
	/// Logs to stderr like msbuild so visual studio prints the error message in errors list
	/// </summary>
	public static class MsBuildLog
	{
		/// <summary>
		/// LogWarning
		/// </summary>
		/// <param name="errorCode"></param>
		/// <param name="description"></param>
		public static void LogWarning(string errorCode, string description)
		{
			LogMessage("warning", errorCode, description);
		}


		/// <summary>
		/// LogError
		/// </summary>
		/// <param name="errorCode"></param>
		/// <param name="description"></param>
		public static void LogError(string errorCode, string description)
		{
			LogMessage("error", errorCode, description);
		}


		private static void LogMessage(string messageType, string errorCode, string description)
		{
			// https://stackoverflow.com/questions/3704549/visual-studio-post-build-event-throwing-errors
			// FilePath[(LineNumber[,ColumnNumber])]: MessageType[ MessageCode]: Description
			// E:\Projects\SampleProject\Program.cs(18,5): error CS1519: Invalid token '}' in class, struct, or interface member declaration

			var myPath = Assembly.GetExecutingAssembly().Location;

			Console.Error.WriteLine($"{myPath}: {messageType} {RemoveNewLines(errorCode)}: {RemoveNewLines(description)}");
		}


		private static string RemoveNewLines(string s)
		{
			if (string.IsNullOrEmpty(s))
				return "";

			s = s.Replace("\r\n", " ");
			s = s.Replace("\n", " ");

			return s;
		}
	}
}
