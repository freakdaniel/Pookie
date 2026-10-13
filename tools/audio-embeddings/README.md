# Optional model preparation

Pookie builds, runs, learns and tests with C# and ONNX Runtime. The verified ONNX
model and independent reference fixture are already in the repository. No Python
installation is required for these operations.

These developer scripts are only needed when deliberately regenerating the model
or its independent numerical reference. `prepare_musicnn.py` converts the official
TensorFlow GraphDef using tf2onnx, then compares TensorFlow and ONNX outputs.
`reference_input.py` generates an independent NumPy frontend/reference fixture.
`prepare_moods.py` converts four MTG mood classification heads into one ONNX graph,
checks TensorFlow/ONNX agreement on raw outputs of the bundled encoder, and adds
mood predictions to the same independent frontend fixture. The classification
heads must receive raw embeddings; normalised search vectors are incompatible.

To regenerate, use an isolated Python 3.11 environment outside the application:

```powershell
py -3.11 -m venv artifacts/musicnn-conversion/venv
artifacts/musicnn-conversion/venv/Scripts/python -m pip install -r tools/audio-embeddings/requirements.txt
artifacts/musicnn-conversion/venv/Scripts/python tools/audio-embeddings/prepare_musicnn.py
artifacts/musicnn-conversion/venv/Scripts/python tools/audio-embeddings/reference_input.py
artifacts/musicnn-conversion/venv/Scripts/python tools/audio-embeddings/prepare_moods.py
dotnet test tests/Pookie.Playback.Tests
```

Review the regenerated model hash against `MusicnnEncoder.ModelId` before using a
new model; a changed encoder must not silently reuse old vectors or audio centers.
Python.NET would still require a compatible Python runtime and the same converter
packages. It would add interop and interpreter lifetime management to a one-time
conversion without removing those dependencies, so it is not used here.

Model attribution and full license: `NOTICE` and
`licenses/CC-BY-NC-SA-4.0.txt` at the repository root.
