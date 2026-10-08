// LapCont — Android — Hilt application and repository bindings
// License: MIT
package com.lapcont.mobile
import android.app.Application
import android.content.Context
import androidx.room.Room
import com.lapcont.mobile.data.*
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.android.HiltAndroidApp
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import javax.inject.Singleton
import kotlinx.coroutines.launch

/** OS-owned application; no automatic capture or connection at process startup. */
@HiltAndroidApp class LapContApplication : Application() {
    @javax.inject.Inject lateinit var repository: PcRepository
    override fun onCreate() {
        super.onCreate()
        if (!BuildConfig.OPTIONAL_PUSH) return
        val receiver = object : android.content.BroadcastReceiver() {
            override fun onReceive(context: Context, intent: android.content.Intent) {
                val pc = intent.getStringExtra("pc_id") ?: return
                repository.scope.launch { repository.verifyPushHint(pc) }
            }
        }
        val filter = android.content.IntentFilter("com.lapcont.VERIFY_EVENT_HINT")
        androidx.core.content.ContextCompat.registerReceiver(this,receiver,filter,androidx.core.content.ContextCompat.RECEIVER_NOT_EXPORTED)
    }
}
/** Singleton metadata database; networking and cryptographic lifetimes belong to PcRepository. */
@Module @InstallIn(SingletonComponent::class)
object DataModule {
    @Provides @Singleton fun database(@ApplicationContext context: Context): PcDatabase = Room.databaseBuilder(context, PcDatabase::class.java, "pcs.db").build()
    @Provides fun dao(database: PcDatabase): PcDao = database.pcs()
}
