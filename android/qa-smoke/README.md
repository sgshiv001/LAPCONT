# Finite physical release UI driver

This Java-only instrumentation APK targets the explicitly selected `com.lapcont.app`
process and operates only that application's UI. It uses Android's
own accessibility actions, requires explicit opt-in, and checks the owner's saved
LAN pairing, repeated Refresh and the normal Live View Start/Stop controls. It has
no runtime dependency on the product's Kotlin or obfuscated application classes.
It neither grants permissions nor saves camera, audio, screenshots or UI trees.

Build with `:qa-smoke:assembleDebug`, install the finite helper, then run
`com.lapcont.qa/.ReleaseSmokeInstrumentation` with
`-e optimizedUiQa true -e qaPcName <paired-PC-name>`. Android stops the target product
process at instrumentation boundaries. Reopen the normal app after the finite test;
the test preserves its data and pairing. The driver returns a metadata-only report
in the instrumentation result. Decoder frame counts, audibility and latency are
not inferred from clicking the Start/Stop controls.

Do not run against an unrelated phone or a different product package. The helper
does not read the product's private files, bypass Android permission dialogs or
change its enrollment. Restore the retained normal build after optimized QA.
