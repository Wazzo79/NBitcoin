﻿using NBitcoin.Crypto;
using NBitcoin.DataEncoders;
using Org.BouncyCastle.Math;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;

namespace NBitcoin
{
	public class BitField
	{
		byte[] _Rawform;
		byte[] _Mask;
		private readonly int _BitCount;
		public int BitCount
		{
			get
			{
				return _BitCount;
			}
		}
		public int ByteCount
		{
			get
			{
				return _Rawform.Length;
			}
		}

		public byte[] Mask
		{
			get
			{
				return _Mask;
			}
		}

		public BitField(byte[] rawform, int bitcount)
		{
			_BitCount = bitcount;

			var byteCount = GetPrefixByteLength(bitcount);
			if(rawform.Length == byteCount)
				_Rawform = rawform.ToArray();
			if(rawform.Length < byteCount)
				_Rawform = rawform.Concat(new byte[byteCount - rawform.Length]).ToArray();
			if(rawform.Length > byteCount)
				_Rawform = rawform.Take(rawform.Length - byteCount).ToArray();

			_Mask = new byte[byteCount];
			int bitleft = bitcount;

			for(int i = 0 ; i < byteCount ; i++)
			{
				var numberBits = Math.Min(8, bitleft);
				_Mask[i] = (byte)((1 << numberBits) - 1);
				bitleft -= numberBits;
				if(bitleft == 0)
					break;
			}
		}
		public BitField(uint encodedForm, int bitcount)
			: this(Utils.ToBytes(encodedForm, true), bitcount)
		{

		}

		public static int GetPrefixByteLength(int bitcount)
		{
			if(bitcount > 32)
				throw new ArgumentException("Bitcount should be less or equal to 32", "bitcount");
			if(bitcount == 0)
				return 0;
			return Math.Min(4, bitcount / 8 + 1);
		}

		public byte[] GetRawForm()
		{
			return _Rawform.ToArray();
		}

		public uint GetEncodedForm()
		{
			var encoded =
				_Rawform.Length == 4 ? _Rawform : _Rawform.Concat(new byte[4 - _Rawform.Length]).ToArray();

			return Utils.ToUInt32(encoded, true);
		}

		public bool Match(uint value)
		{
			var data = Utils.ToBytes(value, true);
			if(data.Length * 8 < _BitCount)
				return false;

			for(int i = 0 ; i < _Mask.Length ; i++)
			{
				if((data[i] & _Mask[i]) != (_Rawform[i] & _Mask[i]))
					return false;
			}
			return true;
		}



		public StealthPayment[] GetPayments(Transaction transaction)
		{
			List<StealthPayment> result = new List<StealthPayment>();
			for(int i = 0 ; i < transaction.Outputs.Count ; i++)
			{
				var metadata = StealthMetadata.TryParse(transaction.Outputs[i].ScriptPubKey);
				if(metadata != null && Match(metadata))
				{
					result.Add(new StealthPayment(transaction.Outputs[i + 1].ScriptPubKey, metadata));
				}
			}
			return result.ToArray();
		}

		public bool Match(StealthMetadata metadata)
		{
			return Match(metadata.BitField);
		}
	}
	public class BitcoinStealthAddress : Base58Data
	{

		public BitcoinStealthAddress(string base58, Network network)
			: base(base58, network)
		{
		}
		public BitcoinStealthAddress(byte[] raw, Network network)
			: base(raw, network)
		{
		}


		public BitcoinStealthAddress(PubKey scanKey, PubKey[] pubKeys, int signatureCount, BitField bitfield, Network network)
			: base(GenerateBytes(scanKey, pubKeys, bitfield, signatureCount), network)
		{
		}





		public byte Options
		{
			get;
			private set;
		}

		public byte SignatureCount
		{
			get;
			set;
		}

		public PubKey ScanPubKey
		{
			get;
			private set;
		}

		public PubKey[] SpendPubKeys
		{
			get;
			private set;
		}

		public BitField Prefix
		{
			get;
			private set;
		}

		protected override bool IsValid
		{
			get
			{
				try
				{
					MemoryStream ms = new MemoryStream(vchData);
					this.Options = (byte)ms.ReadByte();
					this.ScanPubKey = new PubKey(ms.ReadBytes(33));
					var pubkeycount = (byte)ms.ReadByte();
					List<PubKey> pubKeys = new List<PubKey>();
					for(int i = 0 ; i < pubkeycount ; i++)
					{
						pubKeys.Add(new PubKey(ms.ReadBytes(33)));
					}
					SpendPubKeys = pubKeys.ToArray();
					SignatureCount = (byte)ms.ReadByte();

					var bitcount = (byte)ms.ReadByte();
					var byteLength = BitField.GetPrefixByteLength(bitcount);

					var prefix = ms.ReadBytes(byteLength);
					Prefix = new BitField(prefix, bitcount);
				}
				catch(Exception)
				{
					return false;
				}
				return true;
			}
		}
		private static byte[] GenerateBytes(PubKey scanKey, PubKey[] pubKeys, BitField bitField, int signatureCount)
		{
			MemoryStream ms = new MemoryStream();
			ms.WriteByte(0); //Options
			ms.Write(scanKey.Compress().ToBytes(), 0, 33);
			ms.WriteByte((byte)pubKeys.Length);
			foreach(var key in pubKeys)
			{
				ms.Write(key.Compress().ToBytes(), 0, 33);
			}
			ms.WriteByte((byte)signatureCount);
			if(bitField == null)
				ms.Write(new byte[] { 0 }, 0, 1);
			else
			{
				ms.WriteByte((byte)bitField.BitCount);
				var raw = bitField.GetRawForm();
				ms.Write(raw, 0, raw.Length);
			}
			return ms.ToArray();
		}

		public override Base58Type Type
		{
			get
			{
				return Base58Type.STEALTH_ADDRESS;
			}
		}


		public StealthPayment CreatePayment(Key ephemKey = null)
		{
			if(ephemKey == null)
				ephemKey = new Key();

			var metadata = StealthMetadata.CreateMetadata(ephemKey, this.Prefix);
			return new StealthPayment(SignatureCount, SpendPubKeys, ephemKey, ScanPubKey, metadata);
		}

		public StealthNonce GetNonce(Key receiverKey, PubKey senderKey)
		{
			//var nonce = new StealthNonce(receiverKey, senderKey, PubKey, Network);
			//if(!nonce.DeriveKey(receiverKey))
			//{
			//	throw new SecurityException("invalid receiver key for this nonce");
			//}
			//return nonce;
			return null;
		}



	}
}
