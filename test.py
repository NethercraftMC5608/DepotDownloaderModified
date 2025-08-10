import os
import json
import time
import tempfile
import threading
import subprocess
from pathlib import Path

POLL_INTERVAL = 1.0           # seconds between reads


def poll_progress(path, stop_event):
    last_pct = None
    while not stop_event.is_set():
        if not path.exists():
            time.sleep(0.1); continue
        try:
            with path.open("r", encoding="utf-8") as f:
                data = json.load(f)
        except (json.JSONDecodeError, PermissionError):
            time.sleep(0.1); continue

        pct = float(data.get("percentage", 0))
        downloaded = int(data.get("downloaded", 0))
        total = int(data.get("total", 0))

        if pct != last_pct:
            if total > 0:
                print(f"{pct:6.2f}%  ({downloaded}/{total} bytes) YIPEEE!")
            else:
                # percent-only mode (still useful!)
                print(f"{pct:6.2f}%  (percent-only)")
            last_pct = pct

        if pct >= 100:
            print("Download complete.")
            stop_event.set()

        time.sleep(0.1)



def download_depot(args):
    # 1️⃣ temp file for JSON progress
    tmp = tempfile.NamedTemporaryFile(delete=False)
    progress_path = Path(tmp.name)
    tmp.close()  # release the Windows handle

    # 2️⃣ background thread setup
    stop_event = threading.Event()
    t = threading.Thread(target=poll_progress, args=(progress_path, stop_event))
    t.start()

    try:
        # 3️⃣ environment + command
        env = os.environ.copy()
        env["DEPOTDOWNLOADER_PROGRESS_FILE"] = str(progress_path)

        cmd = [
            # use either DLL or EXE, not both
            "./DepotDownloader",              # or "dotnet", "DepotDownloader.dll"
            "-app",      args["app_id"],
            "-pubfile",  args["manifest_id"],
            "-username", args["username"],
            "-password", args["password"],
        ]

        proc = subprocess.Popen(cmd, env=env)

        # 4️⃣ wait until the process ends OR the JSON thread signals done
        while proc.poll() is None and not stop_event.is_set():
            time.sleep(0.5)

        if proc.poll() is None:
            # JSON said finished first → terminate child gracefully
            proc.terminate()
            proc.wait()

    finally:
        stop_event.set()   # make sure the thread ends
        t.join(timeout=2)
        try:
            progress_path.unlink(missing_ok=True)
        except Exception:
            pass


if __name__ == "__main__":
    download_depot(
        {
            "app_id":      "730",
            "username":    "WhitneyThiel960",
            "password":    "Sean0987021",
            "manifest_id": "3536622725",
        }
    )

