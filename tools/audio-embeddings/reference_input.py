"""Independent NumPy frontend and ONNX golden output for the C# integration test."""
import json
from pathlib import Path
import numpy as np
import onnxruntime as ort

root = Path(__file__).resolve().parents[2]
samples = (.2 * np.sin(2 * np.pi * 220 * np.arange(48000) / 16000)
           + .1 * np.sin(2 * np.pi * 660 * np.arange(48000) / 16000)).astype(np.float32)
def mel(hz):
    return hz / (200 / 3) if hz < 1000 else 15 + np.log(hz / 1000) / (np.log(6.4) / 27)
def hz(value):
    return value * (200 / 3) if value < 15 else 1000 * np.exp((value - 15) * np.log(6.4) / 27)
edges = np.array([hz(i * mel(8000) / 97) for i in range(98)])
frequencies = np.arange(257) * 16000 / 512
filters = np.array([np.maximum(0, np.minimum((frequencies - edges[i]) / (edges[i + 1] - edges[i]),
                 (edges[i + 2] - frequencies) / (edges[i + 2] - edges[i + 1]))) * 2 / (edges[i + 2] - edges[i]) for i in range(96)])
frames = []
padded = np.pad(samples, (256, 512))
for frame in range(187):
    spectrum = np.fft.rfft(padded[frame * 256:frame * 256 + 512] * np.hanning(512))
    frames.append(np.log10(1 + 10000 * (filters @ np.abs(spectrum) ** 2)))
input_value = np.array(frames, dtype=np.float32)[None]
session = ort.InferenceSession(str(root / "src/Pookie.Recommendations/Models/musicnn-msd.onnx"), providers=["CPUExecutionProvider"])
embedding = session.run(None, {"model/Placeholder:0": input_value})[0][0]
embedding /= np.linalg.norm(embedding)
dest = root / "tests/Fixtures/Audio"
dest.mkdir(parents=True, exist_ok=True)
(dest / "musicnn-reference.json").write_text(json.dumps({"frame30": input_value[0, 30].tolist(), "embedding": embedding.tolist()}, indent=2) + "\n")
