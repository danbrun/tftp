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

		//
		// Creates a new packet object
		//
		// @params _size The size in bytes of the packet
		// @params _data The byte array of packet data
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

			// Copy the swapped bits to the data field
			data = new byte[size];
			bitSwap.CopyTo(data, 0);
		}

		//
		// Validates the Hamming code of the packet and corrects 1 bit errors
		//
		// @returns True if the packet has no errors or was correct, false if packet must be resent
		public bool Validate()
		{
			// Get a bit array of the data
			var bits = new BitArray(data);

			// Iterate over each word (4 bytes) in the data
			for (int word = 0; word < size - 1; word += 4)
			{
				// Keep track of the parity and counts
				var errors = new bool[] { false, false, false, false, false };
				var counts = new byte[] { 1, 2, 4, 8, 16 };

				for (var c = 0; c < 5; c++)
				{
					int bit = 32 - counts[c];
					while (bit > 0)
					{
						// Get the parity for the word
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

				// Get the position of a 1 bit error
				var pos = 0;
				for (var c = 0; c < 5; c++)
				{
					if (errors[c])
					{
						pos += counts[c];
					}
				}

				// Flip the 1 bit error if it exists
				if (pos != 0)
				{
					var flip = (word * 8) + (32 - pos);
					bits[flip] = !bits[flip];
				}

				// Calculate the overall parity
				var parity = false;
				for (var bit = 0; bit < 32; bit++)
				{
					if (bits[(word * 8) + (31 - bit)])
					{
						parity = !parity;
					}
				}

				// If the overall parity is wrong, the packet is invalid
				if (parity)
				{
					return false;
				}
			}

			// Copy the correct bits back into the data field
			data = new byte[size];
			bits.CopyTo(data, 0);
			return true;
		}

		//
		// Extracts the file data from the packet, discards the Hamming bits
		//
		// @returns A byte array with the packet data
		public byte[] Extract()
		{
			// Get counters and bit arrays
			var pos = 0;
			var outSize = size * 13 / 16;
			var bits = new BitArray(data);
			var extract = new BitArray(outSize * 8);

			// Copy all bits that aren't in a Hamming bit position
			for (var bit = 0; pos < extract.Length; bit++)
			{
				var i = bit % 32;
				if (i != 0 && i != 16 && i != 24 && i != 28 && i != 30 && i != 31)
				{
					extract[pos++] = bits[bit];
				}
			}

			// Copy to a byte array and return it
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
			// Requests the file
			RRQ(file);

			// Opens the file on the disk
			using (var disk = new FileStream(file, FileMode.Create))
			{

				while (true)
				{
					// Get the current packet
					var packet = DATA();

					// If this packet gives an error, print it
					if (packet.opcode == 5)
					{
						Console.WriteLine("Error: " + Encoding.ASCII.GetString(packet.data, 2, packet.data.Length - 2));
						return;
					}

					// Validate the packet data
					if (!packet.Validate())
					{
						// Ask for the packet again if it was invalid
						NACK(packet.block);
						continue;
					}

					// Extract the data from the packet
					var extracted = packet.Extract();

					// Trim trailing bytes that are equal to 0
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

					// Writes this packets data to the file
					disk.Write(extracted, 0, extracted.Length - trim);
					disk.Flush();

					// Stop if this was the last packet
					if (packet.size < 512)
					{
						return;
					}

					// Request the next packet
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
			// Gets the first valid IPv4 address
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

			// Creates  UDP socket with the given endpoint
			dest = new IPEndPoint(ip, 7000);
			socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

			// Sends the appropriate file request
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
