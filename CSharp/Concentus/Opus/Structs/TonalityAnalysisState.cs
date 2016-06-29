﻿using Concentus.Celt.Structs;
using Concentus.Common;
using Concentus.Common.CPlusPlus;
using Concentus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Concentus.Structs
{
    internal class TonalityAnalysisState
    {
        internal readonly float[] angle = new float[240];
        internal readonly float[] d_angle = new float[240];
        internal readonly float[] d2_angle = new float[240];
        internal readonly int[] inmem = new int[OpusConstants.ANALYSIS_BUF_SIZE];
        internal int mem_fill;                      /* number of usable samples in the buffer */
        internal readonly float[] prev_band_tonality = new float[OpusConstants.NB_TBANDS];
        internal float prev_tonality;
        internal readonly Pointer<Pointer<float>> E = Arrays.InitTwoDimensionalArrayPointer<float>(OpusConstants.NB_FRAMES, OpusConstants.NB_TBANDS);
        internal readonly float[] lowE = new float[OpusConstants.NB_TBANDS];
        internal readonly float[] highE = new float[OpusConstants.NB_TBANDS];
        internal readonly float[] meanE = new float[OpusConstants.NB_TOT_BANDS];
        internal readonly float[] mem = new float[32];
        internal readonly float[] cmean = new float[8];
        internal readonly float[] std = new float[9];
        internal float music_prob;
        internal float Etracker;
        internal float lowECount;
        internal int E_count;
        internal int last_music;
        internal int last_transition;
        internal int count;
        internal readonly float[] subframe_mem = new float[3];
        internal int analysis_offset;
        /** Probability of having speech for time i to DETECT_SIZE-1 (and music before).
            pspeech[0] is the probability that all frames in the window are speech. */
        internal readonly float[] pspeech = new float[OpusConstants.DETECT_SIZE];
        /** Probability of having music for time i to DETECT_SIZE-1 (and speech before).
            pmusic[0] is the probability that all frames in the window are music. */
        internal readonly float[] pmusic = new float[OpusConstants.DETECT_SIZE];
        internal float speech_confidence;
        internal float music_confidence;
        internal int speech_confidence_count;
        internal int music_confidence_count;
        internal int write_pos;
        internal int read_pos;
        internal int read_subframe;
        internal readonly AnalysisInfo[] info = new AnalysisInfo[OpusConstants.DETECT_SIZE];

        internal TonalityAnalysisState()
        {
            for (int c = 0; c < OpusConstants.DETECT_SIZE; c++)
            {
                info[c] = new AnalysisInfo();
            }
        }

        internal void Reset()
        {
            Arrays.MemSet<float>(angle,0, 240);
            Arrays.MemSet<float>(d_angle,0, 240);
            Arrays.MemSet<float>(d2_angle,0, 240);
            Arrays.MemSet<int>(inmem, 0, OpusConstants.ANALYSIS_BUF_SIZE);
            mem_fill = 0;
            Arrays.MemSet<float>(prev_band_tonality,0, OpusConstants.NB_TBANDS);
            prev_tonality = 0;
            for (int c = 0; c < OpusConstants.NB_FRAMES; c++)
            {
                E[c].MemSet(0, OpusConstants.NB_TBANDS);
            }
            Arrays.MemSet<float>(lowE,0, OpusConstants.NB_TBANDS);
            Arrays.MemSet<float>(highE,0, OpusConstants.NB_TBANDS);
            Arrays.MemSet<float>(meanE,0, OpusConstants.NB_TOT_BANDS);
            Arrays.MemSet<float>(mem,0, 32);
            Arrays.MemSet<float>(cmean,0, 8);
            Arrays.MemSet<float>(std,0, 9);
            music_prob = 0;
            Etracker = 0;
            lowECount = 0;
            E_count = 0;
            last_music = 0;
            last_transition = 0;
            count = 0;
            Arrays.MemSet<float>(subframe_mem,0, 3);
            analysis_offset = 0;
            Arrays.MemSet<float>(pspeech,0, OpusConstants.DETECT_SIZE);
            Arrays.MemSet<float>(pmusic,0, OpusConstants.DETECT_SIZE);
            speech_confidence = 0;
            music_confidence = 0;
            speech_confidence_count = 0;
            music_confidence_count = 0;
            write_pos = 0;
            read_pos = 0;
            read_subframe = 0;
            for (int c = 0; c < OpusConstants.DETECT_SIZE; c++)
            {
                info[c].Reset();
            }
        }
    }
}
