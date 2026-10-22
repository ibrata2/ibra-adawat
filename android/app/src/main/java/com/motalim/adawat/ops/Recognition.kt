package com.motalim.adawat.ops

import android.graphics.Bitmap
import com.googlecode.tesseract.android.TessBaseAPI
import com.google.zxing.BinaryBitmap
import com.google.zxing.DecodeHintType
import com.google.zxing.LuminanceSource
import com.google.zxing.MultiFormatReader
import com.google.zxing.NotFoundException
import com.google.zxing.RGBLuminanceSource
import com.google.zxing.common.HybridBinarizer
import com.google.zxing.multi.GenericMultipleBarcodeReader
import com.motalim.adawat.AdawatApp
import java.io.File

/** Text and codes in pictures, read on the phone: Tesseract for Arabic and English, ZXing for QR codes and barcodes. */
object Recognition {
    private val lock = Any()

    /** The bundled models, copied out once because Tesseract reads them from disk. */
    private val dataPath: String by lazy {
        val app = AdawatApp.instance
        val root = File(app.noBackupFilesDir, "ocr")
        val folder = File(root, "tessdata").apply { mkdirs() }
        for (name in app.assets.list("tessdata").orEmpty()) {
            val target = File(folder, name)
            val size = app.assets.openFd("tessdata/$name").use { it.length }
            if (target.length() != size) app.assets.open("tessdata/$name").use { input -> target.outputStream().use { input.copyTo(it) } }
        }
        root.path
    }

    fun text(bitmap: Bitmap, progress: (Double) -> Unit = {}): String = synchronized(lock) {
        val api = TessBaseAPI { progress(it.percent / 100.0) }
        try {
            if (!api.init(dataPath, "ara+eng")) throw IllegalStateException("Text recognition could not start.")
            api.pageSegMode = TessBaseAPI.PageSegMode.PSM_AUTO
            api.setImage(bitmap)
            api.getHOCRText(0) // Runs recognition with progress; the plain text below reuses the result.
            api.utF8Text.orEmpty()
        } finally {
            api.recycle()
        }
    }

    /** Faces, as pixel rectangles, found by Android's own face detector. */
    fun faces(bitmap: Bitmap): List<android.graphics.RectF> {
        // The detector wants RGB 565 with an even width.
        val width = bitmap.width and 1.inv()
        val small = Bitmap.createBitmap(width, bitmap.height, Bitmap.Config.RGB_565)
        android.graphics.Canvas(small).drawBitmap(bitmap, 0f, 0f, null)
        val found = arrayOfNulls<android.media.FaceDetector.Face>(32)
        val count = android.media.FaceDetector(width, bitmap.height, found.size).findFaces(small, found)
        small.recycle()
        return found.take(count).filterNotNull().map { face ->
            val middle = android.graphics.PointF().also { face.getMidPoint(it) }
            val eyes = face.eyesDistance()
            android.graphics.RectF(middle.x - eyes * 1.25f, middle.y - eyes * 1.2f, middle.x + eyes * 1.25f, middle.y + eyes * 1.9f)
        }
    }

    /** Lines of text, as pixel rectangles. */
    fun textBoxes(bitmap: Bitmap): List<android.graphics.RectF> = synchronized(lock) {
        val api = TessBaseAPI()
        try {
            if (!api.init(dataPath, "ara+eng")) return emptyList()
            api.setImage(bitmap)
            api.utF8Text
            val lines = api.textlines
            val boxes = lines.boxRects.map { android.graphics.RectF(it) }
            lines.recycle()
            boxes
        } finally {
            api.recycle()
        }
    }

    fun codes(bitmap: Bitmap): List<String> {
        val w = bitmap.width
        val h = bitmap.height
        val pixels = IntArray(w * h).also { bitmap.getPixels(it, 0, w, 0, 0, w, h) }
        val hints = mapOf(DecodeHintType.TRY_HARDER to true, DecodeHintType.CHARACTER_SET to "UTF-8")
        val reader = GenericMultipleBarcodeReader(MultiFormatReader())
        fun read(source: LuminanceSource) = try {
            reader.decodeMultiple(BinaryBitmap(HybridBinarizer(source)), hints).map { it.text }
        } catch (_: NotFoundException) {
            emptyList()
        }
        val source = RGBLuminanceSource(w, h, pixels)
        // Light codes on dark backgrounds read once inverted.
        return read(source).ifEmpty { read(source.invert()) }.distinct()
    }
}
