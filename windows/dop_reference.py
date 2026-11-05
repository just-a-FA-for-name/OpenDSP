"""Read-only, pre-encoded DoP fixture through independent PortAudio WASAPI.
No decoding, DSP, resampling, volume writes, or software silence padding.
The parent owns endpoint-volume recovery and a kill-on-close child job.
"""
import json
import pathlib
import sys
import threading

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / "build/pythonlibs"))
import sounddevice as sd

raw, report = sys.argv[1:3]
result = {"backend": "PortAudio WASAPI exclusive blocking", "underflows": 0}
try:
    apis = sd.query_hostapis()
    matches = [(i, d) for i, d in enumerate(sd.query_devices())
               if "ACMEE-Phantom Sound 4X" in d["name"] and d["max_output_channels"] >= 2
               and apis[d["hostapi"]]["name"] == "Windows WASAPI"]
    if len(matches) != 1:
        raise RuntimeError("Expected exactly one Phantom 4X WASAPI endpoint")
    index, device = matches[0]
    settings = sd.WasapiSettings(exclusive=True, auto_convert=False, explicit_sample_format=True)
    # Loading before playback keeps file I/O and synthesis off the output path.
    data = pathlib.Path(raw).read_bytes()
    stream = sd.RawOutputStream(device=index, samplerate=176400, channels=2, dtype="int32",
                                latency=.2, extra_settings=settings)
    result.update(device=device["name"], actual_rate=stream.samplerate, latency=stream.latency)
    print("READY", flush=True)
    if sys.stdin.readline().strip() != "PLAY":
        raise RuntimeError("Parent did not authorize playback")
    cancelled = threading.Event()
    def parent_lifetime():
        sys.stdin.readline()
        cancelled.set()
    threading.Thread(target=parent_lifetime, daemon=True).start()
    try:
        stream.start()
        block = 8192 * 8
        view = memoryview(data)
        for offset in range(0, len(data), block):
            if cancelled.is_set():
                raise RuntimeError("Parent disconnected")
            result["underflows"] += int(stream.write(view[offset:offset + block]))
        stream.stop()
    finally:
        stream.close()
    result["result"] = "TRANSPORT_COMPLETED"
except Exception as error:
    result.update(result="FAIL", error=repr(error))
    print("ERROR " + repr(error), flush=True)
finally:
    pathlib.Path(report).write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding="utf-8")
sys.exit(0 if result["result"] == "TRANSPORT_COMPLETED" else 1)
