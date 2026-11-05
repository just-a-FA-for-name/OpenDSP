package org.openeq.app;
/** Native USB access is independent of Android AudioTrack. Probe never claims or resets a device. */
final class UsbNative {
 static {System.loadLibrary("usb1.0");System.loadLibrary("openeq_usb");}
 static native String version();
 static native String probe(int descriptor)throws java.io.IOException;
 static native long open(int descriptor,int rate,int bits,boolean dop)throws java.io.IOException;
 static native void writePcm(long handle,double[] samples,int count)throws java.io.IOException;
 static native void writeDop(long handle,byte[] payload,int count)throws java.io.IOException;
 static native String status(long handle)throws java.io.IOException;
 static native void finish(long handle)throws java.io.IOException;
 static native void cancel(long handle);
 static native String awaitStopped(long handle,int timeoutMs)throws java.io.IOException;
 static native void attach(int descriptor,int interfaceId);
 static native void close(long handle)throws java.io.IOException;
 static native long srcOpen(int inputRate,int outputRate,boolean adaptive)throws java.io.IOException;
 static native void srcPush(long handle,double[] input,int frames)throws java.io.IOException;
 static native int srcRead(long handle,double[] output,int frames)throws java.io.IOException;
 static native void srcClock(long handle,double ppm)throws java.io.IOException;
 static native void srcFinish(long handle)throws java.io.IOException;
 static native void srcClose(long handle);
}
