"""Convert the official MSD MusiCNN embedding head; Python is a build tool only.

Run in an isolated environment with tensorflow-cpu 2.16.2, numpy 1.26.4,
tf2onnx 1.17.0, onnx 1.16.2 and onnxruntime. Runtime inference in Pookie uses C# ONNX Runtime.
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
SOURCE = "https://essentia.upf.edu/models/feature-extractors/musicnn/msd-musicnn-1.pb"
DEST.mkdir(parents=True, exist_ok=True)
with urllib.request.urlopen(SOURCE, timeout=60) as response:
    graph_bytes = response.read(32 * 1024 * 1024 + 1)
if len(graph_bytes) > 32 * 1024 * 1024:
    raise ValueError("Unexpected model size")
graph = tf.compat.v1.GraphDef()
graph.ParseFromString(graph_bytes)
model_path = DEST / "musicnn-msd.onnx"
tf2onnx.convert.from_graph_def(
    graph, input_names=["model/Placeholder:0"],
    output_names=["model/dense/BiasAdd:0"],
    shape_override={"model/Placeholder:0": [None, 187, 96]},
    opset=17, output_path=str(model_path))
session = ort.InferenceSession(str(model_path), providers=["CPUExecutionProvider"])
sample = np.random.default_rng(1).random((1, 187, 96), dtype=np.float32)
actual = session.run(None, {"model/Placeholder:0": sample})[0]
with tf.Graph().as_default() as reference_graph:
    tf.import_graph_def(graph, name="")
    with tf.compat.v1.Session(graph=reference_graph) as reference:
        expected = reference.run(reference_graph.get_tensor_by_name("model/dense/BiasAdd:0"),
                                 {reference_graph.get_tensor_by_name("model/Placeholder:0"): sample})
np.testing.assert_allclose(actual, expected, rtol=2e-4, atol=2e-4)
if actual.shape != (1, 200) or not np.isfinite(actual).all():
    raise ValueError("Invalid embedding output")
metadata = {"source": SOURCE, "source_sha256": hashlib.sha256(graph_bytes).hexdigest(),
            "onnx_sha256": hashlib.sha256(model_path.read_bytes()).hexdigest(),
            "dimensions": 200, "sample_rate": 16000, "frames": 187, "bands": 96,
            "license": "CC-BY-NC-SA-4.0", "attribution": "Music Technology Group, Universitat Pompeu Fabra",
            "conversion": "TensorFlow embedding head to ONNX opset 17",
            "max_reference_error": float(np.max(np.abs(actual - expected)))}
(DEST / "musicnn-msd.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"bytes": model_path.stat().st_size, "reference_error": metadata["max_reference_error"]}))
