using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace POGOLib
{
    internal class Encrypt
    {
        /*
        ******************************
        signature in encrypt.h:
        ******************************
        extern int encrypt(const unsigned char* input, size_t input_size,
            const unsigned char* iv, size_t iv_size,
            unsigned char* output, size_t * output_size);
        ******************************
        Implementation in encrypt.c
        ******************************
        int encrypt(const unsigned char *input, size_t input_size,
	        const unsigned char* iv, size_t iv_size,
	        unsigned char* output, size_t * output_size) {
	        unsigned char arr2[256];
	        unsigned char arr3[256];
	        size_t roundedsize, totalsize;
	        if (iv_size != 32){
		        return -1;
	        }
	        roundedsize = input_size + (256 - (input_size % 256));
	        totalsize = roundedsize + 32;
	        if (output == NULL){
		        *output_size = totalsize;
		        return 0;
	        }
	        if (*output_size < totalsize){
		        *output_size = totalsize;
		        return -1;
	        }
	        for (int j = 0; j < 8; j++){
		        for (int i = 0; i < 32; i++){
			        arr2[32 * j + i] = rotl8(iv[i], j); //rotate byte left
		        }
	        }
	        memcpy(output, iv, 32);
	        memcpy(output + 32, input, input_size);
	        if (roundedsize > input_size)
	        {
		        memset(output + 32 + input_size, 0, roundedsize - input_size); //pad data with zeroes
	        }
	        output[totalsize - 1] = 256 - (input_size % 256);
	        for (size_t offset = 32; offset < totalsize; offset += 256)
	        {
		        for (int i = 0; i < 256; i++){
			        output[offset + i] ^= arr2[i];
		        }
		        sub_9E9D8(output + offset, arr3); // !! encryption here
		        memcpy(arr2, arr3, 256);
		        memcpy(output + offset, arr3, 256);
	        }
	        *output_size = totalsize;
	        return 0;
        }
        ******************************
        How python calls it
        ******************************
        def _generate_signature(self, signature_plain, lib_path="encrypt.so"): //signature_plain is a string
            if self._signature_lib is None:
                self.activate_signature(lib_path)
            self._signature_lib.argtypes = [ctypes.c_char_p, ctypes.c_size_t, ctypes.c_char_p, ctypes.c_size_t, ctypes.POINTER(ctypes.c_ubyte), ctypes.POINTER(ctypes.c_size_t)]
            self._signature_lib.restype = ctypes.c_int

            iv = os.urandom(32)

            output_size = ctypes.c_size_t()

            self._signature_lib.encrypt(signature_plain, len(signature_plain), iv, 32, None, ctypes.byref(output_size))
            output = (ctypes.c_ubyte * output_size.value)()
            self._signature_lib.encrypt(signature_plain, len(signature_plain), iv, 32, ctypes.byref(output), ctypes.byref(output_size))
            signature = b''.join(list(map(lambda x: six.int2byte(x), output)))
            return signature
        ******************************
        [01:24:17] <jcotton> char* would be either string or char[]
        [01:24:28] <jcotton> size_t would be IntPtr
        [01:24:49] <jcotton> all other pointers would be ref or out and then the pointed to type

            */

        public static string DoEncryption(string signature_plain)
        {
            // simulating "iv = os.urandom(32)" in python -> docs say "Return a string of n random bytes suitable for cryptographic use."
            byte[] bytes = new byte[32];
            new Random().NextBytes(bytes);
            char[] iv = new char[32];
            for (int i = 0; i < 32; i++)
                iv[i] = Convert.ToChar(bytes[i]); //should be same as a direct type cast

            // what size do I instantiate it to??
            //Python does it like this "output = (ctypes.c_ubyte * ctypes.c_size_t().value)()" ??
            //ctypes.c_size_t().value == 0L So what does "output = (ctypes.c_ubyte * 0L)()" do??
            //ctypes.c_ubyte * 4 creates an unsigned char array of length 4 so the above line creates an empty unsigned char array
            //class ctypes.c_ubyte Represents the C unsigned char datatype, it interprets the value as small integer.The constructor accepts an optional integer initializer; no overflow checking is done.
            // so small integer I take is a a short? ushort?
            //ushort[] output;
            char[] output;
            IntPtr output_size = new IntPtr();

            Encrypt.encrypt(signature_plain, new IntPtr(signature_plain.Length), iv, new IntPtr(32), out output, output_size); //in python output is None
            output = new char[output_size.ToInt32()];
            Encrypt.encrypt(signature_plain, new IntPtr(signature_plain.Length), iv, new IntPtr(32), out output, output_size);

            byte[] bytes2 = new byte[32];
            for (int i = 0; i < 32; i++)
                bytes2[i] = Convert.ToByte(output[i]); //should be same as a direct type cast

            var signatue = new string(output);

            return signatue;
        }

        [DllImport("encrypt.dll")]
        private static extern int encrypt(string input, IntPtr input_size, char[] iv, IntPtr iv_size, out char[] output, IntPtr output_size);
    }
}