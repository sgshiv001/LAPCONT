// LapCont — Android — Current permissions and observed phone proximity preferences
// License: MIT
package com.lapcont.mobile.proximity

import com.lapcont.mobile.data.PairedPc
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged

/** A fresh authenticated grant overrides the pairing snapshot; offline BLE retains the last known grant. */
internal fun proximityCandidates(
    pcs: Flow<List<PairedPc>>,
    verifiedGrants: Flow<Map<String, Int>>
): Flow<List<PairedPc>> = combine(pcs, verifiedGrants) { paired, grants ->
    paired.filter { it.proximityEnabled && (grants[it.id] ?: it.grants) and 2 != 0 }
}.distinctUntilChanged()
