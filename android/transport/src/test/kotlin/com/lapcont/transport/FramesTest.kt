// LapCont — Tests — Kotlin media interoperability and bounds
// License: MIT
package com.lapcont.transport
import org.junit.Assert.*
import org.junit.Test

/** Isolated wire-format regression tests; fixture is shared with C# FramingTests. */
class FramesTest {
    private val vector = "01010001000102030405060708090A0B0C0D0E0F000000000000002A000000000000823500000006000000016588"
    @Test fun sharedVector() {
        val expected = vector
        val frame = MediaFrame(1, 1, ByteArray(16) { it.toByte() }, 42, 33333, Frames.unhex("000000016588"))
        assertEquals(expected, Frames.hex(Frames.encode(frame)))
        assertArrayEquals(frame.payload, Frames.decode(Frames.unhex(expected)).payload)
    }
    @Test fun badLength() {
        val valid = Frames.unhex(vector)
        assertThrows(IllegalArgumentException::class.java) { Frames.decode(valid.copyOf(valid.size - 1)) }
        val wrongLength = valid.copyOf().apply { this[39] = 7 }
        assertThrows(IllegalArgumentException::class.java) { Frames.decode(wrongLength) }
    }
    @Test fun payloadBounds() { assertThrows(IllegalArgumentException::class.java) { Frames.encode(MediaFrame(2, 0, ByteArray(16), 1, 0, ByteArray(Frames.MEDIA_LIMIT + 1))) } }
}
