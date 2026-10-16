package za.co.auraone.assistant.bridge;

import android.os.Binder;
import android.os.IBinder;
import android.os.Parcel;
import android.os.RemoteException;
import java.util.concurrent.TimeUnit;

/**
 * Shizuku UserService entry point. Runs as Shizuku's shell identity, not the app.
 * Its IPC accepts exactly three named operations; no arbitrary commands/arguments.
 */
public final class BabyPrivilegedService extends Binder {
    public BabyPrivilegedService() {}

    @Override
    protected boolean onTransact(int code, Parcel data, Parcel reply, int flags)
            throws RemoteException {
        if (code == 16777115) { android.os.Process.killProcess(android.os.Process.myPid()); return true; }
        if (code != IBinder.FIRST_CALL_TRANSACTION) {
            return super.onTransact(code, data, reply, flags);
        }
        String action = data.readString();
        String[] command;
        if ("panel_expand".equals(action)) {
            command = new String[]{"/system/bin/cmd", "statusbar", "expand-settings"};
        } else if ("panel_collapse".equals(action)) {
            command = new String[]{"/system/bin/cmd", "statusbar", "collapse"};
        } else if ("menu_key".equals(action)) {
            command = new String[]{"/system/bin/input", "keyevent", "82"};
        } else {
            reply.writeNoException();
            reply.writeString("Unsupported privileged tool");
            return true;
        }

        Process proc = null;
        String outcome;
        try {
            proc = new ProcessBuilder(command).redirectErrorStream(true).start();
            if (!proc.waitFor(4, TimeUnit.SECONDS)) {
                proc.destroyForcibly();
                outcome = "Shizuku action timed out";
            } else {
                outcome = proc.exitValue() == 0
                        ? "Shizuku action completed"
                        : "Shizuku command not supported by this device";
            }
        } catch (Exception e) {
            outcome = "Shizuku action failed: " + e.getClass().getSimpleName();
        } finally {
            if (proc != null) proc.destroy();
        }
        reply.writeNoException();
        reply.writeString(outcome);
        return true;
    }
}
