// MIT. Finite own-app UI check; never grants permissions or saves camera/audio/UI trees.
package com.lapcont.qa;

import android.app.Activity;
import android.app.Instrumentation;
import android.content.Intent;
import android.os.Bundle;
import android.os.SystemClock;
import android.view.accessibility.AccessibilityNodeInfo;
import org.json.JSONObject;
import java.util.ArrayList;
import java.util.List;

public final class ReleaseSmokeInstrumentation extends Instrumentation {
    private static final String PRODUCT="com.lapcont.app";
    private String pcName;
    private long deadline;
    @Override public void onCreate(Bundle args) {
        super.onCreate(args);
        if(args==null || !"true".equals(args.getString("optimizedUiQa")) || args.getString("qaPcName")==null) {
            Bundle result=new Bundle();result.putString("status","explicit_opt_in_required");finish(Activity.RESULT_CANCELED,result);return;
        }
        pcName=args.getString("qaPcName");start();
    }
    @Override public void onStart() {
        Bundle result=new Bundle();deadline=SystemClock.elapsedRealtime()+60000;
        try {
            Intent launch=getTargetContext().getPackageManager().getLaunchIntentForPackage(PRODUCT);
            if(launch==null)throw new IllegalStateException("Target launch unavailable");
            getTargetContext().startActivity(launch.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_CLEAR_TASK));
            click(pcName);click("Connect / retry");waitFor("Refresh status",20000);
            require(has("Route: LAN"),"Saved LAN route not verified");require(has("Windows: unlocked"),"Unlocked state not verified");
            for(int i=0;i<3;i++){click("Refresh status");require(has("Refresh status"),"Refresh disconnected");}
            click("Open Live View");click("Start permitted live tracks");SystemClock.sleep(5000);
            require(has("Stop live media"),"Live controls unavailable");click("Stop live media");click("Back to PCs");
            require(has("Connect saved PCs"),"Dashboard did not return");
            JSONObject report=new JSONObject();report.put("status","passed");report.put("android_api",android.os.Build.VERSION.SDK_INT);
            report.put("abi",android.os.Build.SUPPORTED_ABIS[0]);report.put("package_debuggable",(getTargetContext().getPackageManager().getApplicationInfo(PRODUCT,0).flags&android.content.pm.ApplicationInfo.FLAG_DEBUGGABLE)!=0);
            report.put("retained_pairing",true);report.put("route","saved physical LAN");report.put("refresh_repetitions",3);
            report.put("normal_ui_start_stop",true);report.put("decoder_frame_count","not collected by UI smoke");report.put("audibility","unverified");report.put("media_recorded",false);
            result.putString("status","passed");result.putString("report",report.toString());finish(Activity.RESULT_OK,result);
        } catch(Exception error) {
            result.putString("status","failed");result.putString("error",error.getClass().getSimpleName());result.putString("detail",error.getMessage());finish(Activity.RESULT_CANCELED,result);
        }
    }
    private AccessibilityNodeInfo root() {
        long until=Math.min(deadline,SystemClock.elapsedRealtime()+10000);
        while(SystemClock.elapsedRealtime()<until){AccessibilityNodeInfo node=getUiAutomation().getRootInActiveWindow();if(node!=null && PRODUCT.contentEquals(node.getPackageName()==null?"":node.getPackageName()))return node;SystemClock.sleep(100);}
        throw new IllegalStateException("Own app window unavailable");
    }
    private void flatten(AccessibilityNodeInfo node,List<AccessibilityNodeInfo> out){
        if(PRODUCT.contentEquals(node.getPackageName()==null?"":node.getPackageName()))out.add(node);
        for(int i=0;i<node.getChildCount();i++){AccessibilityNodeInfo child=node.getChild(i);if(child!=null)flatten(child,out);}
    }
    private List<AccessibilityNodeInfo> nodes(){List<AccessibilityNodeInfo> out=new ArrayList<>();flatten(root(),out);return out;}
    private boolean has(String text){for(AccessibilityNodeInfo node:nodes())if(text.contentEquals(node.getText()==null?"":node.getText()))return true;return false;}
    private void waitFor(String text,long budget){long until=Math.min(deadline,SystemClock.elapsedRealtime()+budget);while(SystemClock.elapsedRealtime()<until){if(has(text))return;SystemClock.sleep(100);}throw new IllegalStateException("Expected own-app label: "+text);}
    private void click(String text){
        long until=Math.min(deadline,SystemClock.elapsedRealtime()+10000);int attempts=0;
        while(SystemClock.elapsedRealtime()<until){
            List<AccessibilityNodeInfo> list=nodes();
            for(AccessibilityNodeInfo node:list)if(text.contentEquals(node.getText()==null?"":node.getText())){
                AccessibilityNodeInfo target=node;while(target!=null && !target.isClickable())target=target.getParent();
                if(target!=null && target.isEnabled() && PRODUCT.contentEquals(target.getPackageName()==null?"":target.getPackageName()) && target.performAction(AccessibilityNodeInfo.ACTION_CLICK)){SystemClock.sleep(250);return;}
            }
            int action=attempts++%8<4?AccessibilityNodeInfo.ACTION_SCROLL_FORWARD:AccessibilityNodeInfo.ACTION_SCROLL_BACKWARD;
            for(AccessibilityNodeInfo node:list)if(node.isScrollable()){node.performAction(action);break;}SystemClock.sleep(150);
        }
        throw new IllegalStateException("Own-app action unavailable: "+text);
    }
    private static void require(boolean condition,String error){if(!condition)throw new IllegalStateException(error);}
}
