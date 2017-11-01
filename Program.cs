using System;

namespace tftp
{
	class Program
	{
		static void Main(string[] args)
		{
			if (args.Length == 0 || args.Length > 3)
			{
				Console.WriteLine("Usage: tftp.exe [error|noerror] tftp-host file");
				return;
			}

			bool errors = false;
			if (args[0].ToLower() == "error")
			{
				errors = true;
			}

			var socket = new TrivialSocket(args[1], errors);
			socket.Download(args[2]);
		}

		public static T[] combine<T>(params T[][] arrays)
		{
			var size = 0;
			foreach (var array in arrays)
			{
				size += array.Length;
			}

			var result = new T[size];
			size = 0;
			foreach (var array in arrays)
			{
				array.CopyTo(result, size);
				size += array.Length;
			}

			return result;
		}
	}
}
