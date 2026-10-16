package za.co.auraone.assistant.bridge;

import android.app.Activity;
import android.content.ComponentName;
import android.content.ServiceConnection;
import android.content.pm.PackageManager;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.os.Parcel;
import rikka.shizuku.Shizuku;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;

/**
 * Binds Shizuku's supported UserService API, not its private newProcess method.
 * The controller upstream requires an explicit user tap before these actions.
 */
final class BabyShizuku {
    private BabyShizuku() {}
    private static final ExecutorService WORK = Executors.newSingleThreadExecutor();
    private static final ScheduledExecutorService TIMER = Executors.newSingleThreadScheduledExecutor();
    private static final Handler MAIN = new Handler(Looper.getMainLooper());
    private static final Object LOCK = new Object();
    private static final List<Request> WAITING = new ArrayList<>();
    private static boolean binding = false;
    private static IBinder remote = null;
    private static final Shizuku.UserServiceArgs SERVICE_ARGS = new Shizuku.UserServiceArgs(
            new ComponentName("za.co.auraone.assistant", "za.co.auraone.assistant.bridge.BabyPrivilegedService"))
            .processNameSuffix("babypriv")
            .tag("mybaby-privileged-v4")
            .version(4)
            .daemon(false);

    interface Done { void finished(String result); }

    private static final class Request {
        final String action;
        final Done callback;
        Request(String action, Done callback) { this.action = action; this.callback = callback; }
    }

    static boolean available() {
        try { return Shizuku.pingBinder(); } catch (Throwable t) { return false; }
    }

    static boolean allowed() {
        try {
            return available() && Shizuku.checkSelfPermission() == PackageManager.PERMISSION_GRANTED;
        } catch (Throwable t) { return false; }
    }

    static String status() {
        if (!available()) return "Shizuku unavailable; ordinary tools still work";
        return allowed() ? "Shizuku connected and permission granted"
                         : "Shizuku connected; permission required";
    }

    static String askPermission(Activity activity) {
        if (!available()) return "Start Shizuku first; other Aura One tools still work";
        if (allowed()) return "Shizuku permission already granted";
        try {
            Shizuku.requestPermission(4707);
            return "Approve the Aura One permission request in Shizuku";
        } catch (Throwable t) {
            return "Unable to request Shizuku permission";
        }
    }

    private static void deliver(Done callback, String result) {
        MAIN.post(() -> callback.finished(result));
    }

    private static void runBound(IBinder binder, Request request) {
        WORK.execute(() -> {
            Parcel data = Parcel.obtain();
            Parcel reply = Parcel.obtain();
            String result;
            try {
                if (!binder.transact(IBinder.FIRST_CALL_TRANSACTION, dataFor(data, request.action), reply, 0)) {
                    result = "Shizuku service did not accept the command";
                } else {
                    reply.readException();
                    result = reply.readString();
                }
            } catch (Exception e) {
                result = "Shizuku connection failed: " + e.getClass().getSimpleName();
                synchronized (LOCK) { if (remote == binder) remote = null; }
            } finally {
                data.recycle();
                reply.recycle();
            }
            deliver(request.callback, result == null ? "No Shizuku result" : result);
        });
    }

    private static Parcel dataFor(Parcel out, String action) {
        out.writeString(action);
        return out;
    }

    private static final ServiceConnection CONNECTION = new ServiceConnection() {
        @Override public void onServiceConnected(ComponentName name, IBinder binder) {
            List<Request> queued;
            synchronized (LOCK) {
                remote = binder;
                binding = false;
                queued = new ArrayList<>(WAITING);
                WAITING.clear();
            }
            for (Request request : queued) runBound(binder, request);
        }
        @Override public void onServiceDisconnected(ComponentName name) {
            synchronized (LOCK) { remote = null; binding = false; }
        }
    };

    static void execute(String action, Done callback) {
        if (!"panel_expand".equals(action) && !"panel_collapse".equals(action)
                && !"menu_key".equals(action)) {
            deliver(callback, "Unsupported privileged tool");
            return;
        }
        if (!allowed()) {
            deliver(callback, status());
            return;
        }
        IBinder binder;
        boolean startBinding = false;
        Request request = new Request(action, callback);
        synchronized (LOCK) {
            binder = remote;
            if (binder == null) {
                WAITING.add(request);
                if (!binding) { binding = true; startBinding = true; }
            }
        }
        if (binder != null) {
            runBound(binder, request);
            return;
        }
        TIMER.schedule(() -> {
            boolean timedOut;
            synchronized (LOCK) { timedOut = WAITING.remove(request); if (WAITING.isEmpty() && remote == null) binding = false; }
            if (timedOut) deliver(callback, "Shizuku service connection timed out");
        }, 12, TimeUnit.SECONDS);
        if (startBinding) {
            try {
                Shizuku.bindUserService(SERVICE_ARGS, CONNECTION);
            } catch (Throwable t) {
                List<Request> queued;
                synchronized (LOCK) {
                    binding = false;
                    queued = new ArrayList<>(WAITING);
                    WAITING.clear();
                }
                for (Request queuedRequest : queued) {
                    deliver(queuedRequest.callback, "Shizuku UserService unavailable: "
                            + t.getClass().getSimpleName());
                }
            }
        }
    }
}
