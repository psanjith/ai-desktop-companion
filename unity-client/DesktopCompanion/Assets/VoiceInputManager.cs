using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

/// <summary>
/// Handles microphone recording, WAV encoding, transcription via the backend,
/// and auto-filling + sending the result as a chat message.
/// Attach to the same GameObject as CompanionController.
/// </summary>
public class VoiceInputManager : MonoBehaviour
{
    // ── Config ────────────────────────────────────────────────────────────────
    private const int    SampleRate    = 16000;
    private const int    MaxRecordSecs = 15;

    // ── State ─────────────────────────────────────────────────────────────────
    private bool        isRecording   = false;
    private AudioClip   recordingClip;
    private string      micDevice;
    private Coroutine   pulseCoroutine;

    // ── References (grabbed at runtime) ──────────────────────────────────────
    private CompanionController controller;
    private string transcribeUrl;

    void Awake()
    {
        controller = GetComponent<CompanionController>();
        transcribeUrl = controller != null
            ? controller.GetApiUrl("/transcribe")
            : "http://127.0.0.1:5001/transcribe";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    public void OnMicButtonPressed()
    {
        if (isRecording)
            StopAndTranscribe();
        else
            StartRecording();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Recording
    // ─────────────────────────────────────────────────────────────────────────

    private void StartRecording()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("[Voice] No microphone detected.");
            return;
        }

        micDevice     = Microphone.devices[0];
        recordingClip = Microphone.Start(micDevice, false, MaxRecordSecs, SampleRate);
        isRecording   = true;

        if (controller != null)
        {
            pulseCoroutine = StartCoroutine(PulseMicButton());
            controller.SetMicActiveLabel(true);
            controller.SetStatusText("Listening... (press M or Mic again to stop)");
        }

        Debug.Log($"[Voice] Recording started on '{micDevice}'");
    }

    private void StopAndTranscribe()
    {
        if (!isRecording) return;

        int samplePos = Microphone.GetPosition(micDevice);
        Microphone.End(micDevice);
        isRecording = false;

        if (pulseCoroutine != null)
        {
            StopCoroutine(pulseCoroutine);
            pulseCoroutine = null;
        }

        // Restore mic button colour and label
        SetMicButtonColor(new Color(0.13f, 0.14f, 0.21f, 0.90f)); // BgButton
        if (controller != null) controller.SetMicActiveLabel(false);

        if (samplePos < 100)
        {
            Debug.Log("[Voice] Recording too short, discarding.");
            return;
        }

        // Trim silence at the end
        float[] samples = new float[samplePos * recordingClip.channels];
        recordingClip.GetData(samples, 0);

        if (controller != null) controller.SetStatusText("Processing...");

        byte[] wav = EncodeWAV(samples, recordingClip.channels, SampleRate);
        StartCoroutine(SendToTranscribe(wav));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Network
    // ─────────────────────────────────────────────────────────────────────────

    private IEnumerator SendToTranscribe(byte[] wavBytes)
    {
        Debug.Log($"[Voice] Sending {wavBytes.Length} bytes to /transcribe");

        var form = new WWWForm();
        form.AddBinaryData("audio", wavBytes, "recording.wav", "audio/wav");

        using (var req = UnityWebRequest.Post(transcribeUrl, form))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Voice] Transcribe error: {req.error}");
                if (controller != null) controller.SetStatusText(""); // clear 'Processing...'
                yield break;
            }

            var resp = JsonUtility.FromJson<TranscribeResponse>(req.downloadHandler.text);
            string text = resp?.text?.Trim() ?? "";

            Debug.Log($"[Voice] Transcript: '{text}'");

            if (!string.IsNullOrEmpty(text))
            {
                // Fill the input field and auto-send
                if (controller == null)
                {
                    Debug.LogError("[Voice] controller is null — VoiceInputManager lost its CompanionController reference!");
                }
                else if (controller.userInputField == null)
                {
                    Debug.LogError("[Voice] userInputField is null — input field not wired in Inspector!");
                }
                else
                {
                    Debug.Log($"[Voice] Sending transcript to chat: '{text}'");
                    controller.SetStatusText("You: " + text);
                    controller.userInputField.text = text;
                    controller.OnSendMessage();
                }
            }
            else
            {
                Debug.LogWarning("[Voice] Transcript was empty — nothing sent.");
                if (controller != null) controller.SetStatusText(""); // clear 'Processing...'
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Visual feedback
    // ─────────────────────────────────────────────────────────────────────────

    private IEnumerator PulseMicButton()
    {
        var recordingColor = new Color(0.75f, 0.10f, 0.10f, 1f); // red
        var dimColor       = new Color(0.45f, 0.07f, 0.07f, 1f);

        while (isRecording)
        {
            float t = Mathf.PingPong(Time.time * 2f, 1f);
            SetMicButtonColor(Color.Lerp(dimColor, recordingColor, t));
            yield return null;
        }
    }

    private void SetMicButtonColor(Color c)
    {
        if (controller == null) return;
        var img = controller.MicButtonImage;
        if (img != null) img.color = c;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // WAV encoding (PCM 16-bit)
    // ─────────────────────────────────────────────────────────────────────────

    private static byte[] EncodeWAV(float[] samples, int channels, int sampleRate)
    {
        int dataLen   = samples.Length * 2;        // 16-bit = 2 bytes/sample
        int totalLen  = 44 + dataLen;
        byte[] buf    = new byte[totalLen];

        // RIFF header
        WriteStr(buf,  0, "RIFF");
        WriteI32(buf,  4, totalLen - 8);
        WriteStr(buf,  8, "WAVE");
        WriteStr(buf, 12, "fmt ");
        WriteI32(buf, 16, 16);                     // subchunk size
        WriteI16(buf, 20, 1);                      // PCM
        WriteI16(buf, 22, (short)channels);
        WriteI32(buf, 24, sampleRate);
        WriteI32(buf, 28, sampleRate * channels * 2);
        WriteI16(buf, 32, (short)(channels * 2));  // block align
        WriteI16(buf, 34, 16);                     // bits per sample
        WriteStr(buf, 36, "data");
        WriteI32(buf, 40, dataLen);

        int offset = 44;
        foreach (float s in samples)
        {
            short pcm = (short)Mathf.Clamp(s * 32767f, -32768f, 32767f);
            buf[offset++] = (byte)(pcm & 0xFF);
            buf[offset++] = (byte)(pcm >> 8);
        }

        return buf;
    }

    static void WriteStr(byte[] b, int pos, string s) { foreach (char c in s) b[pos++] = (byte)c; }
    static void WriteI16(byte[] b, int pos, short v)  { b[pos] = (byte)(v & 0xFF); b[pos+1] = (byte)(v >> 8); }
    static void WriteI32(byte[] b, int pos, int v)    { b[pos] = (byte)(v & 0xFF); b[pos+1] = (byte)((v>>8)&0xFF); b[pos+2] = (byte)((v>>16)&0xFF); b[pos+3] = (byte)((v>>24)&0xFF); }

    [System.Serializable]
    private class TranscribeResponse { public string text; }
}
