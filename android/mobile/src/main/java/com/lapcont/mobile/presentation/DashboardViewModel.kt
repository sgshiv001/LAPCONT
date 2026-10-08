// LapCont — Android — ViewModel actions, explicit errors and lifecycle cancellation
// License: MIT
package com.lapcont.mobile.presentation
import android.view.Surface
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.lapcont.mobile.data.*
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.*
import org.json.JSONObject
import javax.inject.Inject

/** UI-thread action coordinator; lifecycle transitions stop capture immediately and prevent pending talk from starting. */
@HiltViewModel
class DashboardViewModel @Inject constructor(val repository: PcRepository) : ViewModel() {
    val pcs = repository.pcs; val states = repository.states; val message = repository.message; val live = repository.livePc
    val error = MutableStateFlow<String?>(null); val pairing = MutableStateFlow(false)
    val fingerprint = MutableStateFlow("Loading device identity…")
    init { viewModelScope.launch(Dispatchers.IO) { try { fingerprint.value=repository.identity.fingerprint() } catch(e:Exception) { error.value="Device identity unavailable (${e.javaClass.simpleName})" } } }
    private var pairJob: Job? = null
    private var talkJob: Job? = null
    private var mediaJob: Job? = null
    fun run(block: suspend () -> Unit) { viewModelScope.launch { try { error.value = null; block() } catch (e: CancellationException) { throw e } catch (e: Exception) { error.value = e.message?.take(220) ?: "Action unavailable" } } }
    fun pair(text: String) { if (pairing.value) return; pairJob = viewModelScope.launch { pairing.value = true; try { repository.enroll(text) } catch (e: CancellationException) { throw e } catch (e: Exception) { error.value = "Pairing failed: ${e.message?.take(180)}" } finally { pairing.value = false } } }
    fun cancelPair() { pairJob?.cancel(); pairing.value = false }
    fun sessionAction(id: String, name: String) = run { repository.action(id, name, JSONObject().put("windows_session_id", repository.session(id))) }
    fun startMedia(id: String, video: Boolean, audio: Boolean, high: Boolean) { mediaJob?.cancel(); mediaJob = viewModelScope.launch { try { repository.startMedia(id, video, audio, high) } catch (e: CancellationException) { throw e } catch (e: Exception) { error.value = e.message } } }
    fun stopMedia() { mediaJob?.cancel(); mediaJob = null; repository.mediaStopImmediately(); run { repository.stopMedia() } }
    fun surface(value: Surface?) = repository.surface(value)
    fun startTalk(id: String) { talkJob?.cancel(); talkJob = viewModelScope.launch { try { repository.startTalk(id) } catch (e: CancellationException) { throw e } catch (e: Exception) { error.value = e.message } } }
    fun stopTalk() { talkJob?.cancel(); talkJob = null; repository.mediaStopTalkImmediately(); run { repository.stopTalk() } }
    fun background() { stopTalk(); stopMedia(); cancelPair() }
}
