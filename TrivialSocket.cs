using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace tftp
{
	class Packet
	{
		public ushort opcode;
		public ushort block;
		public int size;
		public byte[] data;

		public Packet(int _size, byte[] _data)
		{
			opcode = (ushort)_data[1];
			block = (ushort)((_data[2] << 8) + _data[3]); ;
			size = _size - 4;

			// Swap byte ordering
			data = new byte[size];
			for (var i = 0; i < size; i += 4)
			{
				for (var j = 0; j < 4; j++)
				{
					data[i + j] = _data[i + (7 - j)];
				}
			}
		}

		public bool Validate()
		{
			// for (int word = 4; word < size; word += 4)
			// {
			// 	var errors = new bool[5];
			// 	var counts = new byte[] { 1, 2, 4, 8, 16 };
			// 	for (var c = 0; c < 5; c++)
			// 	{
			// 		int sum = 0;
			// 		int bit = counts[c] - 1;
			// 		while (bit < 31)
			// 		{
			// 			for (int i = 0; i < counts[c]; i++)
			// 			{
			// 				sum += data[(word * 8) + (bit - i)] ? 1 : 0;
			// 			}
			// 			bit += counts[c];
			// 		}

			// 		if (sum % 2 == 1)
			// 		{
			// 			errors[c] = true;
			// 		}
			// 	}

			// 	var pos = 0;
			// 	for (var c = 0; c < 5; c++)
			// 	{
			// 		if (errors[c])
			// 		{
			// 			pos += c;
			// 		}
			// 	}
			// }

			return true;
		}

		public byte[] Extract()
		{
			// Swap bit ordering
			var read = new BitArray(data);
			var swap = new BitArray(read.Length);
			for (var i = 0; i < size; i++)
			{
				for (var j = 0; j < 8; j++)
				{
					swap[(i * 8) + j] = read[(i * 8) + (7 - j)];
				}
			}

			var outSize = size * 13 / 16;

			var pos = 0;
			var extract = new BitArray(outSize * 8);
			for (var bit = 0; pos < extract.Length; bit++)
			{
				var i = bit % 32;
				if (i != 0 && i != 16 && i != 24 && i != 28 && i != 30 && i != 31)
				{
					extract[pos++] = swap[bit];
				}
			}

			var bytes = new byte[outSize];
			extract.CopyTo(bytes, 0);
			return bytes;
		}
	}

	class TrivialSocket
	{
		static readonly byte[] octet = Encoding.ASCII.GetBytes("octet");
		static readonly byte[] zero = { 0 };

		string host;
		bool errors = false;
		Socket socket;
		IPEndPoint dest;

		//
		// Constructs a new TFTP socket
		//
		// @param host The host to download from
		// @param errors Whether or not to request with errors
		public TrivialSocket(string host, bool errors)
		{
			this.host = host;
			this.errors = errors;
		}

		//
		// Downloads the file from the host
		//
		// @param file The name of the file to download
		public void Download(string file)
		{
			RRQ(file);

			var offset = 0;
			using (var disk = new FileStream(file, FileMode.Create))
			{

				while (true)
				{
					var packet = DATA();

					if (packet.opcode == 5)
					{
						Console.WriteLine("Error: " + Encoding.ASCII.GetString(packet.data, 2, packet.data.Length - 2));
						return;
					}

					if (!packet.Validate())
					{
						NACK(packet.block);
						continue;
					}

					var extracted = packet.Extract();

					int trim = 0;
					if (packet.size < 512 && packet.size > 0)
					{
						for (int i = 0; i < 4; i++)
						{
							if (extracted[extracted.Length - i - 1] != 0)
							{
								break;
							}
							trim++;
						}
					}

					disk.Write(extracted, 0, extracted.Length - trim);
					disk.Flush();
					offset += extracted.Length;

					if (packet.size < 512)
					{
						break;
					}

					ACK(packet.block);
				}
			}
		}

		//
		// Requests a file by sending an RRQ packet
		//
		// @param file The file to request
		void RRQ(string file)
		{
			var addresses = Dns.GetHostAddresses(host);
			IPAddress ip = addresses[0];
			foreach (var addr in addresses)
			{
				if (addr.AddressFamily == AddressFamily.InterNetwork)
				{
					ip = addr;
					break;
				}
			}

			dest = new IPEndPoint(ip, 7000);
			socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

			var opcode = new byte[] { 0, 1 };
			var name = Encoding.ASCII.GetBytes(file);

			var data = Program.combine<byte>(opcode, name, zero, octet, zero);
			socket.SendTo(data, dest);
		}

		//
		// Reads a UDP packet from the server
		//
		// @returns A tuple with the number of bytes read, block number, and error flag
		Packet DATA()
		{
			var data = new byte[516];
			var length = socket.Receive(data);
			return new Packet(length, data);
		}

		//
		// Sends an acknowledgement in an ACK packet
		//
		// @param block The number of the block received
		void ACK(ushort block)
		{
			var data = new byte[] { 0, 4, (byte)(block >> 8), (byte)block };
			socket.SendTo(data, dest);
		}

		//
		// Sends an error acknowledgement as a NACK
		//
		// @param block The number of the block to redownload
		void NACK(ushort block)
		{
			var data = new byte[] { 0, 6, (byte)(block >> 8), (byte)block };
			socket.SendTo(data, dest);
		}
	}
}
