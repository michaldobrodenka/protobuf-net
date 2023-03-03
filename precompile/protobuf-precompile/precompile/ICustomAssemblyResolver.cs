using System;

namespace ProtoBuf.Precompile
{
	public interface ICustomAssemblyResolver : IDisposable
	{
		string TryResolve(string name);
	}
}
