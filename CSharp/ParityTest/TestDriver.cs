﻿using Concentus;
using Concentus.Common.CPlusPlus;
using Concentus.Opus.Enums;
using Concentus.Structs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ParityTest
{
    public class TestDriver
    {
        private const string OPUS_TARGET_DLL = "opus32-fix.dll";

        [DllImport(OPUS_TARGET_DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr opus_encoder_create(int Fs, int channels, int application, out IntPtr error);

        [DllImport(OPUS_TARGET_DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern void opus_encoder_destroy(IntPtr encoder);

        [DllImport(OPUS_TARGET_DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int opus_encode(IntPtr st, byte[] pcm, int frame_size, IntPtr data, int max_data_bytes);

        [DllImport(OPUS_TARGET_DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int opus_encoder_ctl(IntPtr st, int request, int value);

        private const int OPUS_SET_BITRATE_REQUEST = 4002;
        private const int OPUS_SET_COMPLEXITY_REQUEST = 4010;

        public static string RunTest(TestParameters parameters, short[] inputFile)
        {
            // Create Opus encoder
            IntPtr opusEncoder = IntPtr.Zero;
            IntPtr opusError;
            opusEncoder = opus_encoder_create(parameters.SampleRate, parameters.Channels, parameters.Application, out opusError);
            if ((int)opusError != 0)
                return "There was an error initializing the Opus encoder";

            opus_encoder_ctl(opusEncoder, OPUS_SET_BITRATE_REQUEST, parameters.Bitrate * 1024);
            opus_encoder_ctl(opusEncoder, OPUS_SET_COMPLEXITY_REQUEST, parameters.Complexity);

            // Create Concentus encoder
            BoxedValue<int> concentusError = new BoxedValue<int>();
            OpusEncoder concentusEncoder = opus_encoder.opus_encoder_create(parameters.SampleRate, parameters.Channels, parameters.Application, concentusError);
            if (concentusError.Val != 0)
                return "There was an error initializing the Concentus encoder";

            concentusEncoder.SetBitrate(parameters.Bitrate * 1024);
            concentusEncoder.SetComplexity(parameters.Complexity);
            // concentusEncoder.SetPacketLossPercent(parameters.PacketLossPercent);

            int frameSize = (int)(parameters.FrameSize * parameters.SampleRate / 1000);
            int inputPointer = 0;
            byte[] outputBuffer = new byte[10000];
            short[] inputPacket = new short[frameSize];

            try
            {
                while (inputPointer + frameSize < inputFile.Length)
                {
                    Array.Copy(inputFile, inputPointer, inputPacket, 0, frameSize);
                    inputPointer += frameSize;

                
                        // Encode with Concentus
                        int concentusPacketSize = opus_encoder.opus_encode(concentusEncoder, inputPacket.GetPointer(), frameSize, outputBuffer.GetPointer(), 10000);
                        byte[] concentusEncoded = new byte[concentusPacketSize];
                        Array.Copy(outputBuffer, concentusEncoded, concentusPacketSize);

                    // Encode with Opus
                    byte[] opusEncoded;
                    unsafe
                    {
                        fixed (byte* benc = outputBuffer)
                        {
                            byte[] nextFrameBytes = ShortsToBytes(inputPacket);
                            IntPtr encodedPtr = new IntPtr((void*)(benc));
                            int opusPacketSize = opus_encode(opusEncoder, nextFrameBytes, frameSize, encodedPtr, 10000);
                            if (opusPacketSize != concentusPacketSize)
                                return "Output packet sizes do not match";
                            opusEncoded = new byte[opusPacketSize];
                            Array.Copy(outputBuffer, opusEncoded, opusPacketSize);
                        }
                    }

                    // Check for encoder parity
                    for (int c = 0; c < concentusPacketSize; c++)
                    {
                        if (opusEncoded[c] != concentusEncoded[c])
                            return "Encoded packets do not match";
                    }

                    // Decode with Concentus

                    // Decode with Opus
                }
            }
            catch (ArgumentException e)
            {
                return e.Message;
            }

            return null;
        }

        /// <summary>
        /// Converts interleaved byte samples (such as what you get from a capture device)
        /// into linear short samples (that are much easier to work with)
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static short[] BytesToShorts(byte[] input)
        {
            return BytesToShorts(input, 0, input.Length);
        }

        /// <summary>
        /// Converts interleaved byte samples (such as what you get from a capture device)
        /// into linear short samples (that are much easier to work with)
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static short[] BytesToShorts(byte[] input, int offset, int length)
        {
            short[] processedValues = new short[length / 2];
            for (int c = 0; c < processedValues.Length; c++)
            {
                processedValues[c] = (short)(((int)input[(c * 2) + offset]) << 0);
                processedValues[c] += (short)(((int)input[(c * 2) + 1 + offset]) << 8);
            }

            return processedValues;
        }

        /// <summary>
        /// Converts linear short samples into interleaved byte samples, for writing to a file, waveout device, etc.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static byte[] ShortsToBytes(short[] input)
        {
            return ShortsToBytes(input, 0, input.Length);
        }

        /// <summary>
        /// Converts linear short samples into interleaved byte samples, for writing to a file, waveout device, etc.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static byte[] ShortsToBytes(short[] input, int offset, int length)
        {
            byte[] processedValues = new byte[length * 2];
            for (int c = 0; c < length; c++)
            {
                processedValues[c * 2] = (byte)(input[c + offset] & 0xFF);
                processedValues[c * 2 + 1] = (byte)((input[c + offset] >> 8) & 0xFF);
            }

            return processedValues;
        }
    }
}
