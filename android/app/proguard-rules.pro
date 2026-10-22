# Native bridges: their Java names are part of the C symbols.
-keep class com.googlecode.tesseract.android.** { *; }
-keep class com.motalim.adawat.media.Mp3Encoder { *; }
-keep class com.motalim.adawat.ops.ArchiveNative { *; }

# PdfBox-Android loads fonts and filters by name and has optional JPEG 2000 support.
-keep class com.tom_roush.pdfbox.** { *; }
-keep class com.tom_roush.fontbox.** { *; }
-keep class com.tom_roush.harmony.** { *; }
-dontwarn com.gemalto.jp2.**
-dontwarn org.bouncycastle.**

# ZXing reads its readers by reflection in a few places.
-keep class com.google.zxing.** { *; }
