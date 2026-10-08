// LapCont — Tests — Permission changes and asynchronous Room updates reach advertising
// License: MIT
package com.lapcont.mobile.proximity

import com.lapcont.mobile.data.PairedPc
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.*
import org.junit.Assert.*
import org.junit.Test

class ProximityCandidatesTest {
    private val pc = PairedPc("pc", "PC", "127.0.0.1", 4433, "pin", "sid", 0, proximityEnabled = true)

    @Test fun pcPermissionGrantedAfterPairingStartsEligibility() = runBlocking {
        assertEquals(listOf(pc), proximityCandidates(flowOf(listOf(pc)), flowOf(mapOf(pc.id to 2))).first())
    }

    @Test fun revokedPermissionOverridesOldPairingGrant() = runBlocking {
        assertTrue(proximityCandidates(flowOf(listOf(pc.copy(grants = 2))), flowOf(mapOf(pc.id to 0))).first().isEmpty())
    }

    @Test fun pcGrantDoesNotOptPhoneIntoProximity() = runBlocking {
        assertTrue(proximityCandidates(flowOf(listOf(pc.copy(proximityEnabled = false))), flowOf(mapOf(pc.id to 2))).first().isEmpty())
    }

    @Test fun offlineAdvertisingRetainsPairingGrant() = runBlocking {
        val enabled = pc.copy(grants = 2)
        assertEquals(listOf(enabled), proximityCandidates(flowOf(listOf(enabled)), flowOf(emptyMap())).first())
    }

    @Test fun preferenceUpdateAfterMonitoringStartedIsObserved() = runBlocking {
        val metadata = MutableStateFlow(listOf(pc.copy(proximityEnabled = false)))
        val permissions = MutableStateFlow(mapOf(pc.id to 2))
        val changes = mutableListOf<List<PairedPc>>()
        val collecting = launch(start = CoroutineStart.UNDISPATCHED) {
            proximityCandidates(metadata, permissions).collect { changes.add(it) }
        }
        try {
            withTimeout(2000) { while(changes.isEmpty()) yield() }
            assertTrue(changes.single().isEmpty())
            metadata.value = listOf(pc)
            withTimeout(2000) { while(changes.size < 2) yield() }
            assertEquals(listOf(pc), changes.last())
            permissions.value = mapOf(pc.id to 0)
            withTimeout(2000) { while(changes.size < 3) yield() }
            assertTrue(changes.last().isEmpty())
        } finally { collecting.cancelAndJoin() }
    }
}
