// LapCont — QA — Normal Compose controls, Surface playback and foreground cleanup on the paired phone
package com.lapcont.mobile

import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.ViewModelProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.lapcont.mobile.data.PcRepository
import com.lapcont.mobile.media.LiveMedia
import com.lapcont.mobile.presentation.DashboardViewModel
import com.lapcont.mobile.presentation.MainActivity
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.first
import org.json.JSONObject
import org.junit.*
import org.junit.Assert.*
import org.junit.runner.RunWith
import java.io.File

@RunWith(AndroidJUnit4::class)
class UiProductTests {
    @get:Rule val ui=createAndroidComposeRule<MainActivity>()

    @Test fun pairedPcLiveViewStopAndBackground() = runBlocking<Unit> {
        Assume.assumeTrue("Explicit real-device normal UI test",InstrumentationRegistry.getArguments().getString("normalUiQa")=="true")
        lateinit var repo:PcRepository
        ui.runOnUiThread {repo=ViewModelProvider(ui.activity)[DashboardViewModel::class.java].repository}
        val pc=withTimeout(10000){repo.pcs.first {it.isNotEmpty()}}.first()
        val media=PcRepository::class.java.getDeclaredField("media").let {it.isAccessible=true;it.get(repo) as LiveMedia}
        fun click(text:String){ui.onNodeWithText(text).performScrollTo().performClick()}
        fun status()=runBlocking {repo.action(pc.id,"status").getJSONObject("state").getJSONArray("sessions").getJSONObject(0)}
        try {
            click(pc.name);click("Connect / retry")
            withTimeout(20000){repo.states.first {it[pc.id]?.online==true && it[pc.id]?.verified?.optJSONArray("sessions")?.optJSONObject(0)?.optBoolean("agent_available")==true}}
            assertEquals("LAN",repo.states.value[pc.id]!!.route);assertEquals("unlocked",status().getString("lock_state"))
            click("Open Live View");ui.waitForIdle();click("Start live view")
            withTimeout(20000){while(media.statistics().renderedVideoFrames<20 || media.statistics().decodedAudioPackets<50){delay(100);assertNotNull(repo.livePc.value)}}
            val active=media.statistics();assertEquals("active",status().getString("media_state"))
            val talk=InstrumentationRegistry.getArguments().getString("talkUiQa")=="true"
            var sentTalk=0L
            if(talk){
                val target=ui.onNodeWithText("Hold to talk · release to stop").performScrollTo()
                target.performTouchInput {down(center)};delay(1500);target.performTouchInput {up()}
                withTimeout(5000){while(status().getString("talk_state")!="idle")delay(100)}
                sentTalk=media.statistics().sentTalkPackets
                assertTrue(sentTalk>20)
            }
            click("Stop live view");withTimeout(8000){while(repo.livePc.value!=null)delay(100)};assertEquals("idle",status().getString("media_state"))
            click("Start live view");withTimeout(8000){while(status().getString("media_state")!="active")delay(100)}
            ui.onNodeWithTag("nav-back").performClick()
            withTimeout(8000){while(repo.livePc.value!=null || status().getString("media_state")!="idle")delay(100)}
            ui.onNodeWithText("PC controls").assertExists()
            click("Open Live View");click("Start live view");withTimeout(8000){while(status().getString("media_state")!="active")delay(100)}
            ui.activityRule.scenario.moveToState(Lifecycle.State.CREATED)
            withTimeout(8000){while(repo.livePc.value!=null || status().getString("media_state")!="idle")delay(100)}
            ui.activityRule.scenario.moveToState(Lifecycle.State.RESUMED);delay(1000)
            assertNull(repo.livePc.value);assertEquals("idle",status().getString("media_state"));assertEquals("idle",status().getString("talk_state"))
            File(InstrumentationRegistry.getInstrumentation().targetContext.cacheDir,"product-normal-ui-summary.json").writeText(JSONObject().put("status","passed").put("route","physical saved LAN").put("normal_compose_controls",true).put("surface_video_frames",active.renderedVideoFrames).put("decoded_pc_audio_packets",active.decodedAudioPackets).put("talk_press_release_checked",talk).put("sent_talk_packets",sentTalk).put("stop_checked",true).put("back_stops_live_capture",true).put("background_stops_capture",true).put("resume_does_not_replay",true).put("capture_to_render_latency","unmeasured").put("media_recorded",false).toString())
        } finally {repo.stopMedia();repo.monitoringStop()}
    }
}
