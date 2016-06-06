﻿using Concentus.Celt;
using Concentus.Celt.Structs;
using Concentus.Common;
using Concentus.Common.CPlusPlus;
using Concentus.Opus.Enums;
using Concentus.Silk;
using Concentus.Silk.Structs;
using Concentus.Structs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Concentus
{
    public static class opus_decoder
    {
        public static int opus_decoder_init(OpusDecoder st, int Fs, int channels)
        {
            silk_decoder silk_dec;
            CELTDecoder celt_dec;
            int ret;

            if ((Fs != 48000 && Fs != 24000 && Fs != 16000 && Fs != 12000 && Fs != 8000)
             || (channels != 1 && channels != 2))
                return OpusError.OPUS_BAD_ARG;
            st.Reset();

            /* Initialize SILK encoder */
            silk_dec = st.SilkDecoder;
            celt_dec = st.CeltDecoder;
            st.stream_channels = st.channels = channels;

            st.Fs = Fs;
            st.DecControl.API_sampleRate = st.Fs;
            st.DecControl.nChannelsAPI = st.channels;

            /* Reset decoder */
            ret = dec_API.silk_InitDecoder(silk_dec);
            if (ret != 0) return OpusError.OPUS_INTERNAL_ERROR;

            /* Initialize CELT decoder */
            ret = celt_decoder.celt_decoder_init(celt_dec, Fs, channels);
            if (ret != OpusError.OPUS_OK) return OpusError.OPUS_INTERNAL_ERROR;

            celt_decoder.opus_custom_decoder_ctl(celt_dec, CeltControl.CELT_SET_SIGNALLING_REQUEST, 0);

            st.prev_mode = 0;
            st.frame_size = Fs / 400;
            st.arch = 0;
            return OpusError.OPUS_OK;
        }

        public static OpusDecoder opus_decoder_create(int Fs, int channels, BoxedValue<int> error)
        {
            int ret;
            OpusDecoder st; // porting note: pointer
            if ((Fs != 48000 && Fs != 24000 && Fs != 16000 && Fs != 12000 && Fs != 8000)
             || (channels != 1 && channels != 2))
            {
                if (error != null)
                    error.Val = OpusError.OPUS_BAD_ARG;
                return null;
            }
            st = new OpusDecoder();
            if (st == null)
            {
                if (error != null)
                    error.Val = OpusError.OPUS_ALLOC_FAIL;
                return null;
            }
            ret = opus_decoder_init(st, Fs, channels);
            if (error != null)
                error.Val = ret;
            if (ret != OpusError.OPUS_OK)
            {
                st = null;
            }
            return st;
        }

        public static void smooth_fade(Pointer<int> in1, Pointer<int> in2,
              Pointer<int> output, int overlap, int channels,
      Pointer<int> window, int Fs)
        {
            int i, c;
            int inc = 48000 / Fs;
            for (c = 0; c < channels; c++)
            {
                for (i = 0; i < overlap; i++)
                {
                    int w = Inlines.MULT16_16_Q15(window[i * inc], window[i * inc]);
                    output[i * channels + c] = Inlines.SHR32(Inlines.MAC16_16(Inlines.MULT16_16(w, in2[i * channels + c]),
                                   CeltConstants.Q15ONE - w, in1[i * channels + c]), 15);
                }
            }
        }

        public static int opus_packet_get_mode(Pointer<byte> data)
        {
            int mode;
            if ((data[0] & 0x80) != 0)
            {
                mode = OpusMode.MODE_CELT_ONLY;
            }
            else if ((data[0] & 0x60) == 0x60)
            {
                mode = OpusMode.MODE_HYBRID;
            }
            else {
                mode = OpusMode.MODE_SILK_ONLY;
            }
            return mode;
        }

        public static int opus_decode_frame(OpusDecoder st, Pointer<byte> data,
      int len, Pointer<int> pcm, int frame_size, int decode_fec)
        {
            silk_decoder silk_dec;
            CELTDecoder celt_dec;
            int i, silk_ret = 0, celt_ret = 0;
            ec_ctx dec = new ec_ctx(); // porting note: stack var
            int silk_frame_size;
            int pcm_silk_size;
            Pointer<int> pcm_silk;
            int pcm_transition_silk_size;
            Pointer<int> pcm_transition_silk;
            int pcm_transition_celt_size;
            Pointer<int> pcm_transition_celt;
            Pointer<int> pcm_transition = null;
            int redundant_audio_size;
            Pointer<int> redundant_audio;

            int audiosize;
            int mode;
            int transition = 0;
            int start_band;
            int redundancy = 0;
            int redundancy_bytes = 0;
            int celt_to_silk = 0;
            int c;
            int F2_5, F5, F10, F20;
            Pointer<int> window;
            uint redundant_rng = 0;
            int celt_accum;

            silk_dec = st.SilkDecoder;
            celt_dec = st.CeltDecoder;
            F20 = st.Fs / 50;
            F10 = F20 >> 1;
            F5 = F10 >> 1;
            F2_5 = F5 >> 1;
            if (frame_size < F2_5)
            {

                return OpusError.OPUS_BUFFER_TOO_SMALL;
            }
            /* Limit frame_size to avoid excessive stack allocations. */
            frame_size = Inlines.IMIN(frame_size, st.Fs / 25 * 3);
            /* Payloads of 1 (2 including ToC) or 0 trigger the PLC/DTX */
            if (len <= 1)
            {
                data = null;
                /* In that case, don't conceal more than what the ToC says */
                frame_size = Inlines.IMIN(frame_size, st.frame_size);
            }
            if (data != null)
            {
                audiosize = st.frame_size;
                mode = st.mode;
                EntropyCoder.ec_dec_init(dec, data, (uint)len);
            }
            else {
                audiosize = frame_size;
                mode = st.prev_mode;

                if (mode == 0)
                {
                    /* If we haven't got any packet yet, all we can do is return zeros */
                    for (i = 0; i < audiosize * st.channels; i++)
                        pcm[i] = 0;

                    return audiosize;
                }

                /* Avoids trying to run the PLC on sizes other than 2.5 (CELT), 5 (CELT),
                   10, or 20 (e.g. 12.5 or 30 ms). */
                if (audiosize > F20)
                {
                    do
                    {
                        int ret = opus_decode_frame(st, null, 0, pcm, Inlines.IMIN(audiosize, F20), 0);
                        if (ret < 0)
                        {

                            return ret;
                        }
                        pcm = pcm.Point(ret * st.channels);
                        audiosize -= ret;
                    } while (audiosize > 0);

                    return frame_size;
                }
                else if (audiosize < F20)
                {
                    if (audiosize > F10)
                        audiosize = F10;
                    else if (mode != OpusMode.MODE_SILK_ONLY && audiosize > F5 && audiosize < F10)
                        audiosize = F5;
                }
            }

            /* In fixed-point, we can tell CELT to do the accumulation on top of the
               SILK PCM buffer. This saves some stack space. */
            celt_accum = ((mode != OpusMode.MODE_CELT_ONLY) && (frame_size >= F10)) ? 1 : 0;

            pcm_transition_silk_size = 0;
            pcm_transition_celt_size = 0;
            if (data != null && st.prev_mode > 0 && (
                (mode == OpusMode.MODE_CELT_ONLY && st.prev_mode != OpusMode.MODE_CELT_ONLY && (st.prev_redundancy == 0))
             || (mode != OpusMode.MODE_CELT_ONLY && st.prev_mode == OpusMode.MODE_CELT_ONLY))
               )
            {
                transition = 1;
                /* Decide where to allocate the stack memory for pcm_transition */
                if (mode == OpusMode.MODE_CELT_ONLY)
                    pcm_transition_celt_size = F5 * st.channels;
                else
                    pcm_transition_silk_size = F5 * st.channels;
            }
            pcm_transition_celt = Pointer.Malloc<int>(pcm_transition_celt_size);
            if (transition != 0 && mode == OpusMode.MODE_CELT_ONLY)
            {
                pcm_transition = pcm_transition_celt;
                opus_decode_frame(st, null, 0, pcm_transition, Inlines.IMIN(F5, audiosize), 0);
            }
            if (audiosize > frame_size)
            {
                /*fprintf(stderr, "PCM buffer too small: %d vs %d (mode = %d)\n", audiosize, frame_size, mode);*/

                return OpusError.OPUS_BAD_ARG;
            }
            else {
                frame_size = audiosize;
            }

            /* Don't allocate any memory when in CELT-only mode */
            pcm_silk_size = (mode != OpusMode.MODE_CELT_ONLY && (celt_accum == 0)) ? Inlines.IMAX(F10, frame_size) * st.channels : 0;
            pcm_silk = Pointer.Malloc<int>(pcm_silk_size);

            /* SILK processing */
            if (mode != OpusMode.MODE_CELT_ONLY)
            {
                int lost_flag, decoded_samples;
                Pointer<int> pcm_ptr;

                if (celt_accum != 0)
                    pcm_ptr = pcm;
                else
                    pcm_ptr = pcm_silk;

                if (st.prev_mode == OpusMode.MODE_CELT_ONLY)
                    dec_API.silk_InitDecoder(silk_dec);

                /* The SILK PLC cannot produce frames of less than 10 ms */
                st.DecControl.payloadSize_ms = Inlines.IMAX(10, 1000 * audiosize / st.Fs);

                if (data != null)
                {
                    st.DecControl.nChannelsInternal = st.stream_channels;
                    if (mode == OpusMode.MODE_SILK_ONLY)
                    {
                        if (st.bandwidth == OpusBandwidth.OPUS_BANDWIDTH_NARROWBAND)
                        {
                            st.DecControl.internalSampleRate = 8000;
                        }
                        else if (st.bandwidth == OpusBandwidth.OPUS_BANDWIDTH_MEDIUMBAND)
                        {
                            st.DecControl.internalSampleRate = 12000;
                        }
                        else if (st.bandwidth == OpusBandwidth.OPUS_BANDWIDTH_WIDEBAND)
                        {
                            st.DecControl.internalSampleRate = 16000;
                        }
                        else {
                            st.DecControl.internalSampleRate = 16000;
                            Inlines.OpusAssert(false);
                        }
                    }
                    else {
                        /* Hybrid mode */
                        st.DecControl.internalSampleRate = 16000;
                    }
                }

                lost_flag = data == null ? 1 : 2 * decode_fec;
                decoded_samples = 0;
                do
                {
                    /* Call SILK decoder */
                    int first_frame = (decoded_samples == 0) ? 1 : 0;
                    BoxedValue<int> boxed_frame_size = new BoxedValue<int>();
                    silk_ret = dec_API.silk_Decode(silk_dec, st.DecControl,
                                            lost_flag, first_frame, dec, pcm_ptr, boxed_frame_size, st.arch);
                    silk_frame_size = boxed_frame_size.Val;
                    if (silk_ret != 0)
                    {
                        if (lost_flag != 0)
                        {
                            /* PLC failure should not be fatal */
                            silk_frame_size = frame_size;
                            for (i = 0; i < frame_size * st.channels; i++)
                                pcm_ptr[i] = 0;
                        }
                        else {

                            return OpusError.OPUS_INTERNAL_ERROR;
                        }
                    }
                    pcm_ptr = pcm_ptr.Point(silk_frame_size * st.channels);
                    decoded_samples += silk_frame_size;
                } while (decoded_samples < frame_size);
            }

            start_band = 0;
            if (decode_fec == 0 && mode != OpusMode.MODE_CELT_ONLY && data != null
             && EntropyCoder.ec_tell(dec) + 17 + 20 * (st.mode == OpusMode.MODE_HYBRID ? 1 : 0) <= 8 * len)
            {
                /* Check if we have a redundant 0-8 kHz band */
                if (mode == OpusMode.MODE_HYBRID)
                    redundancy = EntropyCoder.ec_dec_bit_logp(dec, 12);
                else
                    redundancy = 1;
                if (redundancy != 0)
                {
                    celt_to_silk = EntropyCoder.ec_dec_bit_logp(dec, 1);
                    /* redundancy_bytes will be at least two, in the non-hybrid
                       case due to the ec_tell() check above */
                    redundancy_bytes = mode == OpusMode.MODE_HYBRID ?
                                      (int)EntropyCoder.ec_dec_uint(dec, 256) + 2 :
                                      len - ((EntropyCoder.ec_tell(dec) + 7) >> 3);
                    len -= redundancy_bytes;
                    /* This is a sanity check. It should never happen for a valid
                       packet, so the exact behaviour is not normative. */
                    if (len * 8 < EntropyCoder.ec_tell(dec))
                    {
                        len = 0;
                        redundancy_bytes = 0;
                        redundancy = 0;
                    }
                    /* Shrink decoder because of raw bits */
                    dec.storage = (uint)(dec.storage - redundancy_bytes);
                }
            }
            if (mode != OpusMode.MODE_CELT_ONLY)
                start_band = 17;

            {
                int endband = 21;

                switch (st.bandwidth)
                {
                    case OpusBandwidth.OPUS_BANDWIDTH_NARROWBAND:
                        endband = 13;
                        break;
                    case OpusBandwidth.OPUS_BANDWIDTH_MEDIUMBAND:
                    case OpusBandwidth.OPUS_BANDWIDTH_WIDEBAND:
                        endband = 17;
                        break;
                    case OpusBandwidth.OPUS_BANDWIDTH_SUPERWIDEBAND:
                        endband = 19;
                        break;
                    case OpusBandwidth.OPUS_BANDWIDTH_FULLBAND:
                        endband = 21;
                        break;
                }
                celt_decoder.opus_custom_decoder_ctl(celt_dec, CeltControl.CELT_SET_END_BAND_REQUEST, endband);
                celt_decoder.opus_custom_decoder_ctl(celt_dec, CeltControl.CELT_SET_CHANNELS_REQUEST, st.stream_channels);
            }

            if (redundancy != 0)
            {
                transition = 0;
                pcm_transition_silk_size = 0;
            }

            pcm_transition_silk = Pointer.Malloc<int>(pcm_transition_silk_size);

            if (transition != 0 && mode != OpusMode.MODE_CELT_ONLY)
            {
                pcm_transition = pcm_transition_silk;
                opus_decode_frame(st, null, 0, pcm_transition, Inlines.IMIN(F5, audiosize), 0);
            }

            /* Only allocation memory for redundancy if/when needed */
            redundant_audio_size = redundancy != 0 ? F5 * st.channels : 0;
            redundant_audio = Pointer.Malloc<int>(redundant_audio_size);

            /* 5 ms redundant frame for CELT.SILK*/
            if (redundancy != 0 && celt_to_silk != 0)
            {
                celt_decoder.opus_custom_decoder_ctl(celt_dec, CeltControl.CELT_SET_START_BAND_REQUEST, 0);
                celt_decoder.celt_decode_with_ec(celt_dec, data.Point(len), redundancy_bytes,
                                    redundant_audio, F5, null, 0);
                BoxedValue<uint> boxed_finalrange = new BoxedValue<uint>();
                celt_decoder.opus_custom_decoder_ctl(celt_dec, OpusControl.OPUS_GET_FINAL_RANGE_REQUEST, boxed_finalrange);
                redundant_rng = boxed_finalrange.Val;
            }

            /* MUST be after PLC */
            celt_decoder.opus_custom_decoder_ctl(celt_dec, CeltControl.CELT_SET_START_BAND_REQUEST, start_band);

            if (mode != OpusMode.MODE_SILK_ONLY)
            {
                int celt_frame_size = Inlines.IMIN(F20, frame_size);
                /* Make sure to discard any previous CELT state */
                if (mode != st.prev_mode && st.prev_mode > 0 && st.prev_redundancy == 0)
                    celt_decoder.opus_custom_decoder_ctl(celt_dec, OpusControl.OPUS_RESET_STATE);
                /* Decode CELT */
                celt_ret = celt_decoder.celt_decode_with_ec(celt_dec, decode_fec != 0 ? null : data,
                                               len, pcm, celt_frame_size, dec, celt_accum);
            }
            else
            {
                // fixme: make this static
                byte[] silence = { 0xFF, 0xFF };
                if (celt_accum == 0)
                {
                    for (i = 0; i < frame_size * st.channels; i++)
                        pcm[i] = 0;
                }
                /* For hybrid . SILK transitions, we let the CELT MDCT
                   do a fade-out by decoding a silence frame */
                if (st.prev_mode == OpusMode.MODE_HYBRID && !(redundancy != 0 && celt_to_silk != 0 && st.prev_redundancy != 0))
                {
                    celt_decoder.opus_custom_decoder_ctl(celt_dec, CeltControl.CELT_SET_START_BAND_REQUEST, 0);
                    celt_decoder.celt_decode_with_ec(celt_dec, silence.GetPointer(), 2, pcm, F2_5, null, celt_accum);
                }
            }

            if (mode != OpusMode.MODE_CELT_ONLY && celt_accum == 0)
            {
                for (i = 0; i < frame_size * st.channels; i++)
                    pcm[i] = Inlines.SAT16(Inlines.ADD32(pcm[i], pcm_silk[i]));
            }

            {
                BoxedValue<CELTMode> celt_mode = new BoxedValue<CELTMode>();
                celt_decoder.opus_custom_decoder_ctl(celt_dec, CeltControl.CELT_GET_MODE_REQUEST, celt_mode);
                window = celt_mode.Val.window;
            }

            /* 5 ms redundant frame for SILK.CELT */
            if (redundancy != 0 && celt_to_silk == 0)
            {
                celt_decoder.opus_custom_decoder_ctl(celt_dec, OpusControl.OPUS_RESET_STATE);
                celt_decoder.opus_custom_decoder_ctl(celt_dec, CeltControl.CELT_SET_START_BAND_REQUEST, 0);

                celt_decoder.celt_decode_with_ec(celt_dec, data.Point(len), redundancy_bytes, redundant_audio, F5, null, 0);
                BoxedValue<uint> boxed_range = new BoxedValue<uint>(redundant_rng);
                celt_decoder.opus_custom_decoder_ctl(celt_dec, OpusControl.OPUS_GET_FINAL_RANGE_REQUEST, boxed_range);
                redundant_rng = boxed_range.Val;
                smooth_fade(pcm.Point(st.channels * (frame_size - F2_5)), redundant_audio.Point(st.channels * F2_5),
                           pcm.Point(st.channels * (frame_size - F2_5)), F2_5, st.channels, window, st.Fs);
            }
            if (redundancy != 0 && celt_to_silk != 0)
            {
                for (c = 0; c < st.channels; c++)
                {
                    for (i = 0; i < F2_5; i++)
                        pcm[st.channels * i + c] = redundant_audio[st.channels * i + c];
                }
                smooth_fade(redundant_audio.Point(st.channels * F2_5), pcm.Point(st.channels * F2_5),
                            pcm.Point(st.channels * F2_5), F2_5, st.channels, window, st.Fs);
            }
            if (transition != 0)
            {
                if (audiosize >= F5)
                {
                    for (i = 0; i < st.channels * F2_5; i++)
                        pcm[i] = pcm_transition[i];
                    smooth_fade(pcm_transition.Point(st.channels * F2_5), pcm.Point(st.channels * F2_5),
                                pcm.Point(st.channels * F2_5), F2_5,
                                st.channels, window, st.Fs);
                }
                else {
                    /* Not enough time to do a clean transition, but we do it anyway
                       This will not preserve amplitude perfectly and may introduce
                       a bit of temporal aliasing, but it shouldn't be too bad and
                       that's pretty much the best we can do. In any case, generating this
                       transition it pretty silly in the first place */
                    smooth_fade(pcm_transition, pcm,
                                pcm, F2_5,
                                st.channels, window, st.Fs);
                }
            }

            if (st.decode_gain != 0)
            {
                int gain;
                gain = Inlines.celt_exp2(Inlines.MULT16_16_P15(Inlines.QCONST16(6.48814081e-4f, 25), st.decode_gain));
                for (i = 0; i < frame_size * st.channels; i++)
                {
                    int x;
                    x = Inlines.MULT16_32_P16(pcm[i], gain);
                    pcm[i] = Inlines.SATURATE(x, 32767);
                }
            }

            if (len <= 1)
                st.rangeFinal = 0;
            else
                st.rangeFinal = dec.rng ^ redundant_rng;

            st.prev_mode = mode;
            st.prev_redundancy = (redundancy != 0 && celt_to_silk == 0) ? 1 : 0;

            if (celt_ret >= 0)
            {
                if (OPUS_CHECK_ARRAY(pcm, audiosize * st.channels))
                    OPUS_PRINT_INT(audiosize);
            }

            return celt_ret < 0 ? celt_ret : audiosize;

        }

        public static int opus_decode_native(OpusDecoder st, Pointer<byte> data,
          int len, Pointer<int> pcm, int frame_size, int decode_fec,
          int self_delimited, Pointer<int> packet_offset, int soft_clip)
        {
            int i, nb_samples;
            int count, offset;
            byte toc;
            int packet_frame_size, packet_bandwidth, packet_mode, packet_stream_channels;
            /* 48 x 2.5 ms = 120 ms */
            int[] size = new int[48];
            if (decode_fec < 0 || decode_fec > 1)
                return OpusError.OPUS_BAD_ARG;
            /* For FEC/PLC, frame_size has to be to have a multiple of 2.5 ms */
            if ((decode_fec != 0 || len == 0 || data == null) && frame_size % (st.Fs / 400) != 0)
                return OpusError.OPUS_BAD_ARG;
            if (len == 0 || data == null)
            {
                int pcm_count = 0;
                do
                {
                    int ret;
                    ret = opus_decode_frame(st, null, 0, pcm.Point(pcm_count * st.channels), frame_size - pcm_count, 0);
                    if (ret < 0)
                        return ret;
                    pcm_count += ret;
                } while (pcm_count < frame_size);
                Inlines.OpusAssert(pcm_count == frame_size);
                if (OPUS_CHECK_ARRAY(pcm, pcm_count * st.channels))
                    OPUS_PRINT_INT(pcm_count);
                st.last_packet_duration = pcm_count;
                return pcm_count;
            }
            else if (len < 0)
                return OpusError.OPUS_BAD_ARG;

            packet_mode = opus_packet_get_mode(data);
            packet_bandwidth = opus_packet_get_bandwidth(data);
            packet_frame_size = opus.opus_packet_get_samples_per_frame(data, st.Fs);
            packet_stream_channels = opus_packet_get_nb_channels(data);

            BoxedValue<byte> boxed_toc = new BoxedValue<byte>();
            BoxedValue<int> boxed_offset = new BoxedValue<int>();
            BoxedValue<int> boxed_packet_offset = new BoxedValue<int>();
            count = opus.opus_packet_parse_impl(data, len, self_delimited, boxed_toc, null,
                                           size.GetPointer(), boxed_offset, boxed_packet_offset);
            toc = boxed_toc.Val;
            offset = boxed_offset.Val;
            packet_offset = boxed_packet_offset.Val;

            if (count < 0)
                return count;

            data = data.Point(offset);

            if (decode_fec != 0)
            {
                int duration_copy;
                int ret;
                /* If no FEC can be present, run the PLC (recursive call) */
                if (frame_size < packet_frame_size || packet_mode == OpusMode.MODE_CELT_ONLY || st.mode == OpusMode.MODE_CELT_ONLY)
                    return opus_decode_native(st, null, 0, pcm, frame_size, 0, 0, null, soft_clip);
                /* Otherwise, run the PLC on everything except the size for which we might have FEC */
                duration_copy = st.last_packet_duration;
                if (frame_size - packet_frame_size != 0)
                {
                    ret = opus_decode_native(st, null, 0, pcm, frame_size - packet_frame_size, 0, 0, null, soft_clip);
                    if (ret < 0)
                    {
                        st.last_packet_duration = duration_copy;
                        return ret;
                    }
                    Inlines.OpusAssert(ret == frame_size - packet_frame_size);
                }
                /* Complete with FEC */
                st.mode = packet_mode;
                st.bandwidth = packet_bandwidth;
                st.frame_size = packet_frame_size;
                st.stream_channels = packet_stream_channels;
                ret = opus_decode_frame(st, data, size[0], pcm.Point(st.channels * (frame_size - packet_frame_size)),
                      packet_frame_size, 1);
                if (ret < 0)
                    return ret;
                else {
                    if (OPUS_CHECK_ARRAY(pcm, frame_size * st.channels))
                        OPUS_PRINT_INT(frame_size);
                    st.last_packet_duration = frame_size;
                    return frame_size;
                }
            }

            if (count * packet_frame_size > frame_size)
                return OpusError.OPUS_BUFFER_TOO_SMALL;

            /* Update the state as the last step to avoid updating it on an invalid packet */
            st.mode = packet_mode;
            st.bandwidth = packet_bandwidth;
            st.frame_size = packet_frame_size;
            st.stream_channels = packet_stream_channels;

            nb_samples = 0;
            for (i = 0; i < count; i++)
            {
                int ret;
                ret = opus_decode_frame(st, data, size[i], pcm.Point(nb_samples * st.channels), frame_size - nb_samples, 0);
                if (ret < 0)
                    return ret;
                Inlines.OpusAssert(ret == packet_frame_size);
                data = data.Point(size[i]);
                nb_samples += ret;
            }
            st.last_packet_duration = nb_samples;
            if (OPUS_CHECK_ARRAY(pcm, nb_samples * st.channels))
                OPUS_PRINT_INT(nb_samples);

            return nb_samples;
        }

        public static int opus_decode(OpusDecoder st, Pointer<byte> data,
             int len, Pointer<int> pcm, int frame_size, int decode_fec)
        {
            if (frame_size <= 0)
                return OpusError.OPUS_BAD_ARG;
            return opus_decode_native(st, data, len, pcm, frame_size, decode_fec, 0, null, 0);
        }


        //public static int opus_decoder_ctl(OpusDecoder st, int request, params object[] vargs)
        //{
        //    int ret = OPUS_OK;
        //    va_list ap;
        //    void* silk_dec;
        //    CELTDecoder* celt_dec;

        //    silk_dec = (char*)st + st.silk_dec_offset;
        //    celt_dec = (CELTDecoder*)((char*)st + st.celt_dec_offset);


        //    va_start(ap, request);

        //    switch (request)
        //    {
        //        case OPUS_GET_BANDWIDTH_REQUEST:
        //            {
        //                int* value = va_arg(ap, int *);
        //                if (!value)
        //                {
        //                    goto bad_arg;
        //                }
        //                *value = st.bandwidth;
        //            }
        //            break;
        //        case OPUS_GET_FINAL_RANGE_REQUEST:
        //            {
        //                opus_uint32* value = va_arg(ap, opus_uint32 *);
        //                if (!value)
        //                {
        //                    goto bad_arg;
        //                }
        //                *value = st.rangeFinal;
        //            }
        //            break;
        //        case OPUS_RESET_STATE:
        //            {
        //                OPUS_CLEAR((char*)&st.OPUS_DECODER_RESET_START,
        //                      sizeof(OpusDecoder) -
        //                      ((char*)&st.OPUS_DECODER_RESET_START - (char*)st));

        //                celt_decoder.opus_custom_decoder_ctl(celt_dec, OPUS_RESET_STATE);
        //                silk_InitDecoder(silk_dec);
        //                st.stream_channels = st.channels;
        //                st.frame_size = st.Fs / 400;
        //            }
        //            break;
        //        case OPUS_GET_SAMPLE_RATE_REQUEST:
        //            {
        //                int* value = va_arg(ap, int *);
        //                if (!value)
        //                {
        //                    goto bad_arg;
        //                }
        //                *value = st.Fs;
        //            }
        //            break;
        //        case OPUS_GET_PITCH_REQUEST:
        //            {
        //                int* value = va_arg(ap, int *);
        //                if (!value)
        //                {
        //                    goto bad_arg;
        //                }
        //                if (st.prev_mode == OpusMode.MODE_CELT_ONLY)
        //                    celt_decoder.opus_custom_decoder_ctl(celt_dec, OPUS_GET_PITCH(value));
        //                else
        //                    *value = st.DecControl.prevPitchLag;
        //            }
        //            break;
        //        case OPUS_GET_GAIN_REQUEST:
        //            {
        //                int* value = va_arg(ap, int *);
        //                if (!value)
        //                {
        //                    goto bad_arg;
        //                }
        //                *value = st.decode_gain;
        //            }
        //            break;
        //        case OPUS_SET_GAIN_REQUEST:
        //            {
        //                int value = va_arg(ap, int);
        //                if (value < -32768 || value > 32767)
        //                {
        //                    goto bad_arg;
        //                }
        //                st.decode_gain = value;
        //            }
        //            break;
        //        case OPUS_GET_LAST_PACKET_DURATION_REQUEST:
        //            {
        //                opus_uint32* value = va_arg(ap, opus_uint32 *);
        //                if (!value)
        //                {
        //                    goto bad_arg;
        //                }
        //                *value = st.last_packet_duration;
        //            }
        //            break;
        //        default:
        //            /*fprintf(stderr, "unknown opus_decoder_ctl() request: %d", request);*/
        //            ret = OPUS_UNIMPLEMENTED;
        //            break;
        //    }

        //    va_end(ap);
        //    return ret;
        //bad_arg:
        //    va_end(ap);
        //    return OpusError.OPUS_BAD_ARG;
        //}

        public static int opus_packet_get_bandwidth(Pointer<byte> data)
        {
            int bandwidth;
            if ((data[0] & 0x80) != 0)
            {
                bandwidth = OpusBandwidth.OPUS_BANDWIDTH_MEDIUMBAND + ((data[0] >> 5) & 0x3);
                if (bandwidth == OpusBandwidth.OPUS_BANDWIDTH_MEDIUMBAND)
                    bandwidth = OpusBandwidth.OPUS_BANDWIDTH_NARROWBAND;
            }
            else if ((data[0] & 0x60) == 0x60)
            {
                bandwidth = ((data[0] & 0x10) != 0) ? OpusBandwidth.OPUS_BANDWIDTH_FULLBAND :
                                             OpusBandwidth.OPUS_BANDWIDTH_SUPERWIDEBAND;
            }
            else {
                bandwidth = OpusBandwidth.OPUS_BANDWIDTH_NARROWBAND + ((data[0] >> 5) & 0x3);
            }
            return bandwidth;
        }

        public static int opus_packet_get_nb_channels(Pointer<byte> data)
        {
            return ((data[0] & 0x4) != 0) ? 2 : 1;
        }

        public static int opus_packet_get_nb_frames(Pointer<byte> packet, int len)
        {
            int count;
            if (len < 1)
                return OpusError.OPUS_BAD_ARG;
            count = packet[0] & 0x3;
            if (count == 0)
                return 1;
            else if (count != 3)
                return 2;
            else if (len < 2)
                return OpusError.OPUS_INVALID_PACKET;
            else
                return packet[1] & 0x3F;
        }

        public static int opus_packet_get_nb_samples(Pointer<byte> packet, int len,
              int Fs)
        {
            int samples;
            int count = opus_packet_get_nb_frames(packet, len);

            if (count < 0)
                return count;

            samples = count * opus.opus_packet_get_samples_per_frame(packet, Fs);
            /* Can't have more than 120 ms */
            if (samples * 25 > Fs * 3)
                return OpusError.OPUS_INVALID_PACKET;
            else
                return samples;
        }

        public static int opus_decoder_get_nb_samples(OpusDecoder dec,
              Pointer<byte> packet, int len)
        {
            return opus_packet_get_nb_samples(packet, len, dec.Fs);
        }
    }
}
