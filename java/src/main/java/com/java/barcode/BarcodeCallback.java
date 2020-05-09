package com.java.barcode;

import com.dynamsoft.barcode.*;
import java.awt.image.BufferedImage;
public interface BarcodeCallback {
    public void onResult(TextResult[] results, BufferedImage image);
}