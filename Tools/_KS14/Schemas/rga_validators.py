"""
Klovnstation 14's validators for audio attributions.yml files: the engine's (RobustToolbox/Schemas/rga_validators.py),
plus the licences this fork accepts on top. The engine's file is upstream's and is left as it is.

The CI action registers every class in this module by its `tag`, so the engine's own License is kept out of the
module's names: only the one below may claim the "license" tag.
"""
import importlib.util
import os

_ENGINE_VALIDATORS_PATH = os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "RobustToolbox", "Schemas", "rga_validators.py")

_spec = importlib.util.spec_from_file_location("ks_engine_rga_validators", _ENGINE_VALIDATORS_PATH)
_engine = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_engine)


class License(_engine.License):
    # The repository's code is AGPL already; assets from AGPL repositories that state no separate asset licence
    # (Civ13's sounds) come under it.
    licenses = _engine.License.licenses + [
        "AGPL-3.0-only",
        "AGPL-3.0-or-later",
    ]


Url = _engine.Url
