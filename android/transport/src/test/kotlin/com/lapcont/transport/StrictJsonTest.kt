// LapCont — Tests — Strict parser rejects nonstandard/ambiguous control payloads
// License: MIT
package com.lapcont.transport
import org.junit.Assert.*
import org.junit.Test
class StrictJsonTest {
    @Test fun duplicateAndNonstandard() { for (bad in listOf("{\"a\":1,\"a\":2}", "{\"x\":{\"a\":1,\"a\":2}}", "{\"a\":01}", "{\"a\":'x'}", "[]", "{\"a\":true,}")) assertThrows(IllegalArgumentException::class.java) { StrictJson.parse(bad.toByteArray()) } }
    @Test fun utf8AndDepth() { assertThrows(java.nio.charset.CharacterCodingException::class.java) { StrictJson.parse(byteArrayOf(123, 34, 120, 34, 58, 34, -1, 34, 125)) }; assertThrows(IllegalArgumentException::class.java) { StrictJson.parse(("{\"a\":" + "[".repeat(14) + "0" + "]".repeat(14) + "}").toByteArray()) } }
    @Test fun validEscapedAndNested() { val p = StrictJson.parse("{\"a\":{\"b\":true},\"text\":\"a\\\"b\",\"list\":[null,-12.5e2]}".toByteArray()); assertEquals("a\"b",p.getString("text")); assertTrue(p.getJSONObject("a").getBoolean("b")) }
}
