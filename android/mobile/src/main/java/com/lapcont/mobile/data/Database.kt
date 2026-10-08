// LapCont — Android — Room metadata separate from cryptographic material
// License: MIT
package com.lapcont.mobile.data
import androidx.room.*
import kotlinx.coroutines.flow.Flow

/** Non-secret PC metadata; live status never restored as a current verified state. */
@Entity(tableName = "pcs")
data class PairedPc(@PrimaryKey val id: String, val name: String, val addresses: String, val port: Int, val pin: String,
    val windowsSid: String, val grants: Int, val relayUrl: String? = null, val proximityEnabled: Boolean = false)
/** Room serializes database writes; observers receive metadata updates. */
@Dao interface PcDao {
    @Query("SELECT * FROM pcs ORDER BY name") fun observe(): Flow<List<PairedPc>>
    @Query("SELECT * FROM pcs WHERE id=:id") suspend fun get(id: String): PairedPc?
    @Insert(onConflict = OnConflictStrategy.REPLACE) suspend fun save(pc: PairedPc)
    @Query("DELETE FROM pcs WHERE id=:id") suspend fun remove(id: String)
}
/** Only device metadata; backups disabled in the manifest and both backup rule formats. */
@Database(entities = [PairedPc::class], version = 1, exportSchema = true)
abstract class PcDatabase : RoomDatabase() { abstract fun pcs(): PcDao }
