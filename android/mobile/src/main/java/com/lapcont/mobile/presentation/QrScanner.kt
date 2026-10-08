// LapCont — Android — CameraX QR scanner without saving frames
// License: MIT
package com.lapcont.mobile.presentation
import android.content.Context
import androidx.camera.core.*
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.viewinterop.AndroidView
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.google.zxing.*
import com.google.zxing.common.HybridBinarizer
import java.util.concurrent.Executors

/** Camera analysis is a single bounded worker. Every ImageProxy closes; frames are never stored or exported. */
@Composable
fun QrScanner(modifier: Modifier, found: (String) -> Unit) {
    val lifecycle = LocalLifecycleOwner.current
    var provider by remember { mutableStateOf<ProcessCameraProvider?>(null) }
    val executor = remember { Executors.newSingleThreadExecutor() }
    val latest by rememberUpdatedState(found)
    DisposableEffect(lifecycle) { onDispose { provider?.unbindAll(); executor.shutdown() } }
    AndroidView(modifier = modifier, factory = { context ->
        PreviewView(context).also { view ->
            val future = ProcessCameraProvider.getInstance(context)
            future.addListener({
                try {
                    val p = future.get(); provider = p; val preview = Preview.Builder().build().also { it.surfaceProvider = view.surfaceProvider }
                    val analysis = ImageAnalysis.Builder().setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST).build()
                    val reader = MultiFormatReader().apply { setHints(mapOf(DecodeHintType.POSSIBLE_FORMATS to listOf(BarcodeFormat.QR_CODE))) }
                    analysis.setAnalyzer(executor) { frame ->
                        try {
                            val plane = frame.planes[0]; val data = ByteArray(frame.width * frame.height); val source = plane.buffer.duplicate()
                            for (y in 0 until frame.height) { source.position(y * plane.rowStride); for (x in 0 until frame.width) data[y * frame.width + x] = source.get(y * plane.rowStride + x * plane.pixelStride) }
                            val luminance = PlanarYUVLuminanceSource(data, frame.width, frame.height, 0, 0, frame.width, frame.height, false)
                            val result = reader.decodeWithState(BinaryBitmap(HybridBinarizer(luminance))).text
                            if (result.toByteArray().size <= 4096) context.mainExecutor.execute { latest(result) }
                        } catch (_: NotFoundException) { } catch (_: FormatException) { } catch (_: ChecksumException) { }
                        finally { reader.reset(); frame.close() }
                    }
                    p.unbindAll(); p.bindToLifecycle(lifecycle, CameraSelector.DEFAULT_BACK_CAMERA, preview, analysis)
                } catch (e: Exception) { android.util.Log.w("LapCont", "QR camera unavailable: ${e.javaClass.simpleName}") }
            }, context.mainExecutor)
        }
    })
}
