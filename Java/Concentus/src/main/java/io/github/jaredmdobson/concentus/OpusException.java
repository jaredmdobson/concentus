/* Copyright (c) 2016 Logan Stromberg

   Redistribution and use in source and binary forms, with or without
   modification, are permitted provided that the following conditions
   are met:

   - Redistributions of source code must retain the above copyright
   notice, this list of conditions and the following disclaimer.

   - Redistributions in binary form must reproduce the above copyright
   notice, this list of conditions and the following disclaimer in the
   documentation and/or other materials provided with the distribution.

   - Neither the name of Internet Society, IETF or IETF Trust, nor the
   names of specific contributors, may be used to endorse or promote
   products derived from this software without specific prior written
   permission.

   THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
   ``AS IS'' AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
   LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
   A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER
   OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
   EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
   PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
   PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
   LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
   NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
   SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */
package io.github.jaredmdobson.concentus;

/**
 * <p>OpusException class.</p>
 *
 * @version $Id : $Id
 */
public class OpusException extends Exception {

    /**
     * error message
     */
    private String _message;
    /**
     * error code
     */
    private int _opus_error_code;

    /**
     * <p>Constructor for OpusException.</p>
     */
    public OpusException() {
        this("", 0);
    }

    /**
     * <p>Constructor for OpusException.</p>
     *
     * @param message a {@link java.lang.String} object
     */
    public OpusException(String message) {
        this(message, 1);
    }

    /**
     * <p>Constructor for OpusException.</p>
     *
     * @param message         a {@link java.lang.String} object
     * @param opus_error_code a int
     */
    public OpusException(String message, int opus_error_code) {
        _message = message + ": " + CodecHelpers.opus_strerror(opus_error_code);
        _opus_error_code = opus_error_code;
    }

    /** {@inheritDoc} */
    @Override
    public String getMessage() {
        return _message;
    }
}