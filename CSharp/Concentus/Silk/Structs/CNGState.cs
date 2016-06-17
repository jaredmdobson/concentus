﻿using Concentus.Common.CPlusPlus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Concentus.Silk.Structs
{
    /// <summary>
    /// Struct for CNG
    /// </summary>
    public class CNGState
    {
        public readonly Pointer<int> CNG_exc_buf_Q14 = Pointer.Malloc<int>(SilkConstants.MAX_FRAME_LENGTH);
        public readonly Pointer<short> CNG_smth_NLSF_Q15 = Pointer.Malloc<short>(SilkConstants.MAX_LPC_ORDER);
        public readonly Pointer<int> CNG_synth_state = Pointer.Malloc<int>(SilkConstants.MAX_LPC_ORDER);
        public int CNG_smth_Gain_Q16 = 0;
        public int rand_seed = 0;
        public int fs_kHz = 0;

        public void Reset()
        {
            CNG_exc_buf_Q14.MemSet(0, SilkConstants.MAX_FRAME_LENGTH);
            CNG_smth_NLSF_Q15.MemSet(0, SilkConstants.MAX_LPC_ORDER);
            CNG_synth_state.MemSet(0, SilkConstants.MAX_LPC_ORDER);
            CNG_smth_Gain_Q16 = 0;
            rand_seed = 0;
            fs_kHz = 0;
        }
    }
}