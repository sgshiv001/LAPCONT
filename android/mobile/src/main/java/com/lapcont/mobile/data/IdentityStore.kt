// LapCont — Android — Non-exportable Keystore TLS identity and wrapped pairing secrets
// License: MIT
package com.lapcont.mobile.data

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import com.lapcont.transport.Frames
import java.math.BigInteger
import java.net.Socket
import java.security.*
import java.security.cert.X509Certificate
import java.util.Date
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec
import javax.net.ssl.SSLEngine
import javax.net.ssl.X509ExtendedKeyManager
import javax.security.auth.x500.X500Principal
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton

/** Thread-safe Android Keystore adapter. Private TLS key never leaves the OS; unrelated settings live outside this class. */
@Singleton
class IdentityStore @Inject constructor(@ApplicationContext private val context: Context) {
    private val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
    val phoneId: String
        @Synchronized get() { val p = context.getSharedPreferences("identity-id", Context.MODE_PRIVATE); return p.getString("id", null) ?: java.util.UUID.randomUUID().toString().replace("-", "").also { check(p.edit().putString("id", it).commit()) } }
    @Synchronized fun keyManager(): X509ExtendedKeyManager {
        if (!store.containsAlias("lapcont-tls")) {
            KeyPairGenerator.getInstance(KeyProperties.KEY_ALGORITHM_EC, "AndroidKeyStore").apply {
                initialize(KeyGenParameterSpec.Builder("lapcont-tls", KeyProperties.PURPOSE_SIGN or KeyProperties.PURPOSE_VERIFY)
                    // Conscrypt hashes TLS transcripts itself and invokes NONEwithECDSA on non-exportable keys.
                    .setAlgorithmParameterSpec(java.security.spec.ECGenParameterSpec("secp256r1")).setDigests(KeyProperties.DIGEST_NONE, KeyProperties.DIGEST_SHA256)
                    .setCertificateSubject(X500Principal("CN=LapCont Phone"))
                    .setCertificateSerialNumber(BigInteger(128, SecureRandom()))
                    .setCertificateNotBefore(Date(System.currentTimeMillis() - 300000)).setCertificateNotAfter(Date(System.currentTimeMillis() + 2L * 365 * 86400000)).build())
                generateKeyPair()
            }
        }
        val certificate = store.getCertificate("lapcont-tls") as X509Certificate; certificate.checkValidity()
        val privateKey = store.getKey("lapcont-tls", null) as PrivateKey
        require(privateKey.algorithm == "EC") { "This development identity is obsolete; revoke its PC enrollment and reinstall before pairing" }
        return object : X509ExtendedKeyManager() {
            override fun getClientAliases(keyType: String?, issuers: Array<Principal>?) = if (keyType == "EC") arrayOf("lapcont-tls") else emptyArray()
            override fun chooseClientAlias(types: Array<String>?, issuers: Array<Principal>?, socket: Socket?): String? {
                return if (types?.any { it == "EC" } == true) "lapcont-tls" else null
            }
            override fun chooseEngineClientAlias(types: Array<String>?, issuers: Array<Principal>?, engine: SSLEngine?) = chooseClientAlias(types, issuers, null)
            override fun getServerAliases(t: String?, i: Array<Principal>?) = emptyArray<String>()
            override fun chooseServerAlias(t: String?, i: Array<Principal>?, s: Socket?): String? = null
            override fun getCertificateChain(alias: String?) = arrayOf(certificate)
            override fun getPrivateKey(alias: String?) = privateKey
        }
    }
    @Synchronized fun fingerprint(): String { keyManager(); return Frames.hex(MessageDigest.getInstance("SHA-256").digest(store.getCertificate("lapcont-tls").encoded)) }
    @Synchronized private fun wrappingKey(): SecretKey {
        if (!store.containsAlias("lapcont-wrap")) KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore").apply {
            init(KeyGenParameterSpec.Builder("lapcont-wrap", KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build()); generateKey()
        }
        return store.getKey("lapcont-wrap", null) as SecretKey
    }
    @Synchronized fun saveSecret(id: String, bytes: ByteArray) {
        require(id.matches(Regex("[a-fA-F0-9]{32}"))); val cipher = Cipher.getInstance("AES/GCM/NoPadding"); cipher.init(Cipher.ENCRYPT_MODE, wrappingKey()); cipher.updateAAD(id.toByteArray())
        val temporary = java.io.File(context.noBackupFilesDir, "$id.new"); temporary.writeBytes(cipher.iv + cipher.doFinal(bytes)); check(temporary.renameTo(java.io.File(context.noBackupFilesDir, "$id.secret")))
    }
    @Synchronized fun secret(id: String): ByteArray {
        require(id.matches(Regex("[a-fA-F0-9]{32}"))); val wrapped = java.io.File(context.noBackupFilesDir, "$id.secret").readBytes(); require(wrapped.size in 28..8192)
        val cipher = Cipher.getInstance("AES/GCM/NoPadding"); cipher.init(Cipher.DECRYPT_MODE, wrappingKey(), GCMParameterSpec(128, wrapped.copyOfRange(0, 12))); cipher.updateAAD(id.toByteArray()); return cipher.doFinal(wrapped.copyOfRange(12, wrapped.size))
    }
    @Synchronized fun remove(id: String) { require(id.matches(Regex("[a-fA-F0-9]{32}"))); java.io.File(context.noBackupFilesDir, "$id.secret").delete() }
}
