import os
import sys
import numpy as np
import soundfile as sf
from scipy.signal import cheby1, sosfilt, fftconvolve

# ---- Tunables ----

DISTANCE_GAIN = 0.2

EARLY_REFLECTION_DELAY_MS = 40
EARLY_REFLECTION_GAIN = 0.4

LATE_REFLECTION_DELAY_MS = 90
LATE_REFLECTION_GAIN = 0.2

# -------------------

def lowpass(samples, sample_rate, cutoff_hz):
    sos = cheby1(
        N=5,
        rp=0.6,
        Wn=cutoff_hz,
        btype='lowpass',
        fs=sample_rate,
        output='sos'
    )

    if samples.ndim == 1:
        return sosfilt(sos, samples)

    result = np.empty_like(samples)

    for channel in range(samples.shape[1]):
        result[:, channel] = sosfilt(sos, samples[:, channel])

    return result

def make_outdoor_ir(sr):
    length = int(sr * 0.5)

    ir = np.random.normal(0, 1, length)

    decay = np.exp(-np.linspace(0, 8, length))
    ir *= decay

    ir[0] += 1.0

    return ir

def add_outdoor_reflections(samples, sample_rate):
    early_delay = int(sample_rate * EARLY_REFLECTION_DELAY_MS / 1000)
    late_delay = int(sample_rate * LATE_REFLECTION_DELAY_MS / 1000)

    result = samples.copy()

    if samples.ndim == 1:
        if len(samples) > early_delay:
            result[early_delay:] += (
                samples[:-early_delay] * EARLY_REFLECTION_GAIN
            )

        if len(samples) > late_delay:
            result[late_delay:] += (
                samples[:-late_delay] * LATE_REFLECTION_GAIN
            )

    else:
        if len(samples) > early_delay:
            result[early_delay:, :] += (
                samples[:-early_delay, :] * EARLY_REFLECTION_GAIN
            )

        if len(samples) > late_delay:
            result[late_delay:, :] += (
                samples[:-late_delay, :] * LATE_REFLECTION_GAIN
            )

    return result


def process_file(input_path, output_path):
    samples, sample_rate = sf.read(input_path)

    # Ensure float32
    samples = samples.astype(np.float32)

    # Air absorption
    samples = lowpass(
        samples,
        sample_rate,
        3000
    )
    samples = lowpass(
        samples,
        sample_rate,
        2500
    )

    # Distance attenuation
    samples *= DISTANCE_GAIN

    # Outdoor reflections
    samples = add_outdoor_reflections(
        samples,
        sample_rate
    )

    # # ir = make_outdoor_ir(sample_rate)
    # # ir = lowpass(ir, sample_rate, 2500)
    # # samples = fftconvolve(samples, ir, mode="full")
    # Gentle saturation
    samples = np.tanh(samples * 1.2)

    sf.write(
        output_path,
        samples,
        sample_rate,
        format="OGG",
        subtype="VORBIS"
    )


def main():
    if len(sys.argv) != 3:
        print(
            "Usage: python distantify.py <input_dir> <output_dir>"
        )
        return

    input_dir = sys.argv[1]
    output_dir = sys.argv[2]

    os.makedirs(output_dir, exist_ok=True)

    for filename in os.listdir(input_dir):
        if not filename.lower().endswith(".ogg"):
            continue

        input_path = os.path.join(input_dir, filename)
        output_path = os.path.join(output_dir, filename)

        print("Processing", filename)

        process_file(
            input_path,
            output_path
        )

    print("Done")


if __name__ == "__main__":
    main()
