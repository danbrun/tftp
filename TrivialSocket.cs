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
			// Get packet data
			opcode = (ushort)_data[1];
			block = (ushort)((_data[2] << 8) + _data[3]); ;
			size = _size - 4;

			// Swap byte ordering
			var swap = new byte[size];
			for (var i = 0; i < size; i++)
			{
				swap[i] = _data[4 * ((i / 4) + 2) - (i % 4) - 1];
			}

			// Swap bit ordering
			var byteSwap = new BitArray(swap);
			var bitSwap = new BitArray(byteSwap.Length);
			for (var i = 0; i < byteSwap.Length; i++)
			{
				bitSwap[i] = byteSwap[8 * ((i / 8) + 1) - (i % 8) - 1];
			}

			data = new byte[size];
			bitSwap.CopyTo(data, 0);
		}

		public bool Validate()
		{
			var bits = new BitArray(data);

			for (int word = 0; word < size - 1; word += 4)
			{
				var errors = new bool[] { false, false, false, false, false };
				var counts = new byte[] { 1, 2, 4, 8, 16 };
				for (var c = 0; c < 5; c++)
				{
					int bit = 32 - counts[c];
					while (bit > 0)
					{
						for (int i = 0; i < counts[c]; i++)
						{
							if (bits[(word * 8) + (bit - i)])
							{
								errors[c] = !errors[c];
							}
						}
						bit -= 2 * counts[c];
					}
				}

				var pos = 0;
				for (var c = 0; c < 5; c++)
				{
					if (errors[c])
					{
						pos += counts[c];
					}
				}

				if (pos != 0)
				{
					var flip = (word * 8) + (32 - pos);
					bits[flip] = !bits[flip];
				}

				var parity = false;
				for (var bit = 0; bit < 32; bit++)
				{
					if (bits[(word * 8) + (31 - bit)])
					{
						parity = !parity;
					}
				}

				if (parity)
				{
					return false;
				}
			}

			data = new byte[size];
			bits.CopyTo(data, 0);

			return true;
		}

		public byte[] Extract()
		{
			var outSize = size * 13 / 16;

			var pos = 0;
			var bits = new BitArray(data);
			var extract = new BitArray(outSize * 8);
			for (var bit = 0; pos < extract.Length; bit++)
			{
				var i = bit % 32;
				if (i != 0 && i != 16 && i != 24 && i != 28 && i != 30 && i != 31)
				{
					extract[pos++] = bits[bit];
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
						return;
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
			if (errors)
			{
				opcode = new byte[] { 0, 2 };
			}
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
