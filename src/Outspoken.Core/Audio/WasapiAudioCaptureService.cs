using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Outspoken.Core.Audio;

/// <summary>
/// WASAPI shared-mode capture of the default microphone. The device is created on Start
/// and fully disposed on Stop, so the OS mic-in-use indicator clears between dictations
/// (spec acceptance #5). Audio accumulates in a memory buffer only — never disk (ADR-001).
///
/// WASAPI shared mode delivers the device mix format (typically 32-bit float, 44.1/48kHz,
/// 1–2 channels); conversion to mono 16k for Whisper happens once, on Stop, off the
/// capture callback path.
/// </summary>
public sealed class WasapiAudioCaptureService : IAudioCaptureService, IDisposable
{
    private readonly object _gate = new();
    private WasapiCapture? _capture;
    private List<float>? _buffer;
    private WaveFormat? _format;
    private volatile float _currentLevel;

    public float CurrentLevel => _currentLevel;

    public void Start()
    {
        lock (_gate)
        {
            if (_capture is not null)
                throw new InvalidOperationException("Capture already running.");

            var capture = new WasapiCapture(); // default capture endpoint, shared mode
            if (capture.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
            {
                // Shared-mode mix format is float on modern Windows; request it explicitly otherwise.
                capture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(capture.WaveFormat.SampleRate, capture.WaveFormat.Channels);
            }

            _format = capture.WaveFormat;
            _buffer = new List<float>(_format.SampleRate * _format.Channels * 15); // ~15s preallocated
            capture.DataAvailable += OnDataAvailable;
            capture.StartRecording();
            _capture = capture;
        }
    }

    public CapturedAudio Stop()
    {
        WasapiCapture capture;
        List<float> buffer;
        WaveFormat format;

        lock (_gate)
        {
            capture = _capture ?? throw new InvalidOperationException("Capture not running.");
            buffer = _buffer!;
            format = _format!;
            _capture = null;
            _buffer = null;
            _format = null;
        }

        capture.DataAvailable -= OnDataAvailable;
        capture.StopRecording();
        capture.Dispose(); // releases the endpoint — mic indicator clears here
        _currentLevel = 0f;

        return AudioConverter.ToWhisperFormat(buffer.ToArray(), format.Channels, format.SampleRate);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var buffer = _buffer;
        if (buffer is null)
            return;

        var sampleCount = e.BytesRecorded / sizeof(float);
        var sumSquares = 0f;
        for (var i = 0; i < sampleCount; i++)
        {
            var sample = BitConverter.ToSingle(e.Buffer, i * sizeof(float));
            buffer.Add(sample);
            sumSquares += sample * sample;
        }

        if (sampleCount > 0)
            _currentLevel = MathF.Sqrt(sumSquares / sampleCount);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_capture is null)
                return;
            _capture.DataAvailable -= OnDataAvailable;
            _capture.StopRecording();
            _capture.Dispose();
            _capture = null;
            _buffer = null;
        }
    }
}
