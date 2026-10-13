"""Build one small mood head over the existing, unnormalised MusiCNN embeddings.

Developer conversion only; Pookie runs the resulting ONNX model in C#.
"""
import hashlib
import json
from pathlib import Path
import urllib.request

import numpy as np
import tensorflow as tf
import tf2onnx
import onnxruntime as ort

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / "src/Pookie.Recommendations/Models"
names = ["relaxed", "party", "sad", "aggressive"]
positive_classes = [1, 1, 1, 0]
sources = []
with tf.Graph().as_default() as combined:
    embedding = tf.compat.v1.placeholder(tf.float32, [None, 200], name="embedding")
    outputs = []
    for name, positive in zip(names, positive_classes):
        url = f"https://essentia.upf.edu/models/classification-heads/mood_{name}/mood_{name}-msd-musicnn-1.pb"
        with urllib.request.urlopen(url, timeout=60) as response:
            data = response.read(2 * 1024 * 1024 + 1)
        if len(data) > 2 * 1024 * 1024:
            raise ValueError("Unexpected classifier size")
        graph = tf.compat.v1.GraphDef()
        graph.ParseFromString(data)
        prediction = tf.import_graph_def(graph, input_map={"model/Placeholder:0": embedding},
                                        return_elements=["model/Softmax:0"], name=name)[0]
        outputs.append(prediction[:, positive:positive + 1])
        sources.append({"name": name, "source": url, "source_sha256": hashlib.sha256(data).hexdigest(), "positive_class": positive})
    url = "https://essentia.upf.edu/models/classification-heads/deam/deam-msd-musicnn-2.pb"
    with urllib.request.urlopen(url, timeout=60) as response:
        data = response.read(2 * 1024 * 1024 + 1)
    if len(data) > 2 * 1024 * 1024:
        raise ValueError("Unexpected arousal model size")
    graph = tf.compat.v1.GraphDef()
    graph.ParseFromString(data)
    emotion = tf.import_graph_def(graph, input_map={"model/Placeholder:0": embedding},
                                 return_elements=["model/Identity:0"], name="deam")[0]
    outputs.append(emotion)
    sources.append({"name": "deam", "source": url, "source_sha256": hashlib.sha256(data).hexdigest(),
                    "classes": ["valence", "arousal"], "range": [1, 9]})
    moods = tf.concat(outputs, axis=1, name="moods")
    definition = combined.as_graph_def()
    # Verify the head using real, raw outputs of the exact bundled embedding model.
    encoder = ort.InferenceSession(str(DEST / "musicnn-msd.onnx"), providers=["CPUExecutionProvider"])
    input_value = np.random.default_rng(42).random((3, 187, 96), dtype=np.float32) * 3
    raw = encoder.run(None, {"model/Placeholder:0": input_value})[0]
    with tf.compat.v1.Session(graph=combined) as reference:
        expected = reference.run(moods, {embedding: raw})

path = DEST / "musicnn-moods.onnx"
tf2onnx.convert.from_graph_def(definition, input_names=["embedding:0"], output_names=["moods:0"],
                             opset=17, output_path=str(path))
head = ort.InferenceSession(str(path), providers=["CPUExecutionProvider"])
actual = head.run(None, {"embedding:0": raw})[0]
np.testing.assert_allclose(actual, expected, atol=2e-5, rtol=2e-5)
metadata = {"sources": sources, "onnx_sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            "input": "raw msd-musicnn-1 model/dense/BiasAdd:0 (before L2 normalisation)",
            "outputs": names + ["valence", "arousal"], "analysis_version": "musicnn-moods-arousal-v2",
            "license": "CC-BY-NC-SA-4.0", "attribution": "Pablo Alonso; Music Technology Group, Universitat Pompeu Fabra",
            "max_reference_error": float(np.max(np.abs(actual - expected)))}
(DEST / "musicnn-moods.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
# Golden reference uses independent NumPy input preparation, not the C# frontend.
import runpy
globals_reference = runpy.run_path(str(ROOT / "tools/audio-embeddings/reference_input.py"))
fixture = json.loads((ROOT / "tests/Fixtures/Audio/musicnn-reference.json").read_text())
raw_fixture = encoder.run(None, {"model/Placeholder:0": globals_reference["input_value"]})[0]
fixture["moods"] = head.run(None, {"embedding:0": raw_fixture})[0][0].tolist()
(ROOT / "tests/Fixtures/Audio/musicnn-reference.json").write_text(json.dumps(fixture, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"bytes": path.stat().st_size, "reference_error": metadata["max_reference_error"]}))
