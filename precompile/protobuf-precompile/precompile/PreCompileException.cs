using System;

namespace ProtoBuf.Precompile
{
	/// <summary>
	/// Internal exception
	/// </summary>
	public class PreCompileException : Exception
	{
		/// <summary>
		/// 
		/// </summary>
		/// <param name="message"></param>
		public PreCompileException(string message)
			: base(message)
		{
		}
	}
}
