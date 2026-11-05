package org.openeq.dsp;
import java.io.*;
/** Stereo-normalized floating-point source; read returns frame count, zero means EOF. */
public interface PcmSource extends Closeable {
 int rate();int bits();int read(double[] stereo,int frames)throws IOException;
}
