// LapCont — Transport — Strict UTF-8 JSON syntax before platform object parsing
// License: MIT
package com.lapcont.transport
import java.nio.ByteBuffer
import java.nio.charset.CodingErrorAction
import org.json.JSONObject
import org.json.JSONTokener

/** Stateless LPC1 parser. Rejects duplicate keys, invalid UTF-8, nonstandard JSON and excessive nesting before use. */
object StrictJson {
    fun parse(bytes: ByteArray): JSONObject {
        require(bytes.size in 1..Frames.CONTROL_LIMIT)
        val text = Charsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(bytes)).toString()
        Validator(text).run(); return JSONObject(text)
    }
    fun exact(json: JSONObject, vararg fields: String) { require(json.length() == fields.size && json.keys().asSequence().all { it in fields }) { "Unexpected JSON fields" } }
    private class Validator(val s: String) {
        var at = 0
        fun space() { while (at < s.length && s[at] in " \r\n\t") at++ }
        fun consume(c: Char) { space(); require(at < s.length && s[at++] == c) }
        fun string(): String {
            space(); val start = at; consume('"')
            while (at < s.length) {
                val c = s[at++]; require(c.code >= 32)
                if (c == '"') return JSONTokener(s.substring(start, at)).nextValue() as String
                if (c == '\\') { require(at < s.length); val e = s[at++]; require(e in "\"\\/bfnrtu"); if (e == 'u') { require(at + 4 <= s.length && s.substring(at, at + 4).all { it in "0123456789abcdefABCDEF" }); at += 4 } }
            }
            error("Unterminated JSON string")
        }
        fun value(depth: Int) {
            require(depth <= 12); space(); require(at < s.length)
            when (s[at]) {
                '{' -> { at++; space(); val keys = hashSetOf<String>(); if (at < s.length && s[at] == '}') { at++; return }
                    while (true) { require(keys.add(string())) { "Duplicate JSON key" }; consume(':'); value(depth + 1); space(); require(at < s.length); if (s[at] == '}') { at++; break }; consume(',') } }
                '[' -> { at++; space(); if (at < s.length && s[at] == ']') { at++; return }; while (true) { value(depth + 1); space(); require(at < s.length); if (s[at] == ']') { at++; break }; consume(',') } }
                '"' -> string()
                else -> { val start = at; while (at < s.length && s[at] !in " ,]}\r\n\t") at++; val token = s.substring(start, at); require(token in listOf("true", "false", "null") || token.matches(Regex("-?(0|[1-9][0-9]*)(\\.[0-9]+)?([eE][+-]?[0-9]+)?"))) }
            }
        }
        fun run() { space(); require(at < s.length && s[at] == '{'); value(1); space(); require(at == s.length) }
    }
}
