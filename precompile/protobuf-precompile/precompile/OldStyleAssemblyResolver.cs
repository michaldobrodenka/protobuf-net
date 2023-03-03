using System;
using System.Linq;

namespace ProtoBuf.Precompile
{
	public class OldStyleAssemblyResolver : ICustomAssemblyResolver
	{
		public void Dispose()
		{
		}


		public string TryResolve(string name)
		{
			var protobufNet = "protobuf-net";

			// do not resolve the protobuf IKVM binary, redirect to protobuf
			if (name.StartsWith(protobufNet, StringComparison.InvariantCultureIgnoreCase))
				name = protobufNet;

			var zz = new []
			{
				new Tuple<string, string>("protobuf-net", @"C:\Users\andrejo\.nuget\packages\protobuf-net\2.3.17\lib\netstandard2.0\protobuf-net.dll"),
				new Tuple<string, string>("Newtonsoft.Json", @"C:\Users\andrejo\.nuget\packages\Newtonsoft.Json\12.0.3\lib\netstandard2.0\Newtonsoft.Json.dll")
			};

			var l = zz.FirstOrDefault(k => string.Equals(k.Item1, name, StringComparison.InvariantCultureIgnoreCase));
			return l?.Item2;
		}
	}
}
