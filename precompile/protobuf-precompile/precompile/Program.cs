using System;

namespace ProtoBuf.Precompile
{
	class Program
	{
		// https://github.com/protobuf-net/protobuf-net
		// last good source state is protobuf-net 2.10 alpha 5
		// there are no precompile changes after 2.10a5, ikvm is removed and it does not build 
		// (checked protobuf-net up to 2.4.6)

		// ikvm is taken from here: https://sourceforge.net/projects/ikvm/
		// only ikvm-reflection is needed


		static int Main(string[] args)
		{
			try
			{
				Console.WriteLine("protobuf-net pre-compiler");
				PreCompileContext ctx;
				if (!CommandLineAttribute.TryParse(args, out ctx))
				{
					return -1;
				}

				if (ctx.Help)
				{
					Console.WriteLine();
					Console.WriteLine();
					Console.WriteLine(ctx.GetUsage());
					return -2;
				}

				if (!ctx.SanityCheck())
					return -3;

				var allGood = ctx.Execute();
				return allGood ? 0 : -4;
			}
			catch (Exception ex)
			{
				var ix = 1;

				while (ex != null)
				{
					var description = $"{ex.GetType().Name}: {ex.Message}";

					MsBuildLog.LogError($"EX{ix:0000}", description);

					// log stack trace separately otherwise it is unreadable
					if (ex.GetType() != typeof(PreCompileException))
						MsBuildLog.LogError($"ST{ix:0000}", $"Exception stack trace: {ex.StackTrace.Trim()}");

					ex = ex.InnerException;

					ix++;
				}

				return -5;
			}
		}
	}
}
