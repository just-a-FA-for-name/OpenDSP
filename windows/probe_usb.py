"""Probe a selected WASAPI endpoint with silence; leaves defaults unchanged.
Requires sounddevice. Real independent shared/exclusive contention is tested below.
"""
import argparse
import json
import pathlib
import sys
import time

local = pathlib.Path(__file__).resolve().parents[1] / "build" / "pythonlibs"
if local.exists():
    sys.path.insert(0, str(local))
import sounddevice as sd

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--device", default="Tears DSP")
    parser.add_argument("--out", default="evidence/windows-usb-probe.json")
    args = parser.parse_args()
    apis = sd.query_hostapis()
    candidates = [(i, d) for i, d in enumerate(sd.query_devices())
                  if args.device.casefold() in d["name"].casefold()
                  and d["max_output_channels"] >= 2
                  and apis[d["hostapi"]]["name"] == "Windows WASAPI"]
    if len(candidates) != 1:
        raise RuntimeError(f"Expected one WASAPI endpoint, found {candidates}")
    index, device = candidates[0]
    results = []
    shared_baseline = False
    shared_baseline_error = None
    try:
        with sd.RawOutputStream(device=index, samplerate=48000, channels=2,
                                dtype="float32", extra_settings=sd.WasapiSettings()) as stream:
            stream.write(bytes(4800 * 2 * 4))
        shared_baseline = True
    except sd.PortAudioError as error:
        shared_baseline_error = str(error)
    for rate in (44100, 48000, 96000, 192000):
        for dtype, width in (("int16", 2), ("int24", 3), ("int32", 4)):
            test = dict(rate=rate, requested_format=dtype, exclusive=True)
            try:
                settings = sd.WasapiSettings(exclusive=True, auto_convert=False, explicit_sample_format=True)
                with sd.RawOutputStream(device=index, samplerate=rate, channels=2,
                                        dtype=dtype, extra_settings=settings) as stream:
                    test["actual_stream_rate"] = stream.samplerate
                    test["latency_seconds"] = stream.latency
                    test["underflow"] = bool(stream.write(bytes(int(rate * .08) * 2 * width)))
                    # Opening a second shared-mode stream while the first owns the
                    # endpoint must fail. A success means contention was not proven.
                    try:
                        with sd.RawOutputStream(device=index, samplerate=48000, channels=2,
                                                dtype="float32", extra_settings=sd.WasapiSettings()):
                            test["shared_contender_blocked"] = False
                    except sd.PortAudioError as error:
                        test["shared_contender_blocked"] = shared_baseline
                        test["contender_error"] = str(error)
                test["opened"] = True
            except sd.PortAudioError as error:
                test.update(opened=False, error=str(error))
            results.append(test)
            time.sleep(.08)
    report = dict(device=device, shared_baseline_opened=shared_baseline, shared_baseline_error=shared_baseline_error,
                  note="Silence-only API/transport test; no acoustic or analog measurement.", results=results)
    path = pathlib.Path(args.out)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    for row in results:
        print(f'{row["rate"]} {row["requested_format"]}: '
              f'opened={row["opened"]} contender_blocked={row.get("shared_contender_blocked")}')
    print(f"Evidence: {path.resolve()}")

if __name__ == "__main__":
    main()
