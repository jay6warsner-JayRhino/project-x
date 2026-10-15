using Android.Content;
using Android.Media;
using Android.Media.Audiofx;
using System.Text.Json;
using System.Threading.Channels;
using Encoding = Android.Media.Encoding;
namespace AuraOne;

// Native PCM capture/playback. GeminiConnection owns the provider protocol.
public sealed class GeminiLive : IDisposable
{
    readonly Context context;readonly Brain brain;readonly Action<JsonElement> onEvent;
    readonly CancellationTokenSource life=new();
    readonly Channel<(int Epoch,byte[] Bytes)> playback=Channel.CreateBounded<(int,byte[])>(
        new BoundedChannelOptions(128){FullMode=BoundedChannelFullMode.DropOldest,SingleReader=false});
    readonly object audioLifetime=new(),playbackLock=new(),stopLock=new();
    GeminiConnection? connection;
    AudioRecord? microphone;AudioTrack? speaker;AcousticEchoCanceler? echo;NoiseSuppressor? noise;
    AudioManager? manager;Mode previousMode;AudioDeviceInfo? previousDevice;bool previousSpeaker;
    volatile bool closed,muted;int epoch;Task? stopping;
    public bool Connected=>!closed&&connection?.Connected==true;
    public CancellationToken Cancellation=>life.Token;
    public GeminiLive(Context context,Brain brain,Action<JsonElement> onEvent){this.context=context;this.brain=brain;this.onEvent=onEvent;}
    public async Task Start()
    {
        if(closed)return;
        var next=new GeminiConnection(brain,KeyVault.Load(context),HandleEvent);connection=next;
        if(closed){next.Dispose();return;}
        await next.Start();
        lock(audioLifetime)
        {
            if(closed)return;
            manager=(AudioManager)context.GetSystemService(Context.AudioService)!;
            previousMode=manager.Mode;manager.Mode=Mode.InCommunication;
            if(OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                previousDevice=manager.CommunicationDevice;
                // Prefer an attached headset; use the speaker for hands-free use otherwise.
                var devices=manager.AvailableCommunicationDevices;
                var output=devices.FirstOrDefault(x=>x.Type==AudioDeviceType.BleHeadset)
                    ??devices.FirstOrDefault(x=>x.Type==AudioDeviceType.BluetoothSco)
                    ??devices.FirstOrDefault(x=>x.Type==AudioDeviceType.WiredHeadset||x.Type==AudioDeviceType.UsbHeadset)
                    ??devices.FirstOrDefault(x=>x.Type==AudioDeviceType.BuiltinSpeaker);
                if(output!=null)manager.SetCommunicationDevice(output);
            }
            else{previousSpeaker=manager.SpeakerphoneOn;manager.SpeakerphoneOn=!(manager.WiredHeadsetOn||manager.BluetoothScoOn);}
            var inputBuffer=Math.Max(6400,AudioRecord.GetMinBufferSize(16000,ChannelIn.Mono,Encoding.Pcm16bit));
            microphone=new AudioRecord(AudioSource.VoiceCommunication,16000,ChannelIn.Mono,Encoding.Pcm16bit,inputBuffer);
            if(microphone.State!=State.Initialized)throw new InvalidOperationException("The microphone could not start.");
            if(AcousticEchoCanceler.IsAvailable){echo=AcousticEchoCanceler.Create(microphone.AudioSessionId);echo?.SetEnabled(true);}
            if(NoiseSuppressor.IsAvailable){noise=NoiseSuppressor.Create(microphone.AudioSessionId);noise?.SetEnabled(true);}
            var outputBuffer=Math.Max(48000,AudioTrack.GetMinBufferSize(24000,ChannelOut.Mono,Encoding.Pcm16bit));
            speaker=new AudioTrack.Builder().SetAudioAttributes(new AudioAttributes.Builder().SetUsage(AudioUsageKind.VoiceCommunication).SetContentType(AudioContentType.Speech).Build())!
                .SetAudioFormat(new AudioFormat.Builder().SetSampleRate(24000).SetChannelMask(ChannelOut.Mono).SetEncoding(Encoding.Pcm16bit).Build())!
                .SetBufferSizeInBytes(outputBuffer)!.SetTransferMode(AudioTrackMode.Stream)!.Build();
            if(closed)return;
            speaker!.Play();microphone.StartRecording();_=Task.Run(PlayAudio);_=Task.Run(RecordAudio);
        }
    }
    void HandleEvent(JsonElement e)
    {
        if(closed)return;
        if(e.TryGetProperty("type",out var type))
        {
            if(type.GetString()=="app.reconnecting")Interrupt();
            if(type.GetString()=="app.error")Dispose();
        }
        if(e.TryGetProperty("serverContent",out var content))
        {
            if(content.TryGetProperty("interrupted",out var interrupted)&&interrupted.ValueKind==JsonValueKind.True)Interrupt();
            if(content.TryGetProperty("modelTurn",out var turn)&&turn.TryGetProperty("parts",out var parts))
                foreach(var part in parts.EnumerateArray())
                    if(part.TryGetProperty("inlineData",out var audio)&&audio.TryGetProperty("mimeType",out var mime)
                        &&mime.GetString()?.StartsWith("audio/pcm")==true&&audio.TryGetProperty("data",out var data))
                        playback.Writer.TryWrite((Volatile.Read(ref epoch),Convert.FromBase64String(data.GetString()!)));
        }
        onEvent(e);
    }
    async Task RecordAudio()
    {
        var buffer=new byte[1280]; // 40 ms, mono PCM16, 16 kHz.
        try
        {
            while(!closed)
            {
                var count=microphone!.Read(buffer,0,buffer.Length);
                if(count<0)throw new InvalidOperationException("Microphone stream stopped.");
                if(count==0||!Connected||muted)continue;
                await connection!.Send(new{realtimeInput=new{audio=new{mimeType="audio/pcm;rate=16000",data=Convert.ToBase64String(buffer,0,count)}}});
            }
        }
        catch(OperationCanceledException){}catch(Exception e){if(!closed)Fail(e.Message);}
    }
    async Task PlayAudio()
    {
        try
        {
            await foreach(var chunk in playback.Reader.ReadAllAsync(life.Token))
                for(var offset=0;offset<chunk.Bytes.Length&&!closed&&chunk.Epoch==Volatile.Read(ref epoch);)
                    lock(playbackLock)
                    {
                        if(chunk.Epoch!=epoch||speaker==null)break;
                        var count=speaker.Write(chunk.Bytes,offset,Math.Min(1200,chunk.Bytes.Length-offset),WriteMode.Blocking);
                        if(count<=0)break;offset+=count;
                    }
        }
        catch(OperationCanceledException){}catch(Exception e){if(!closed)Fail("Audio playback: "+e.Message);}
    }
    void Interrupt()
    {
        Interlocked.Increment(ref epoch);while(playback.Reader.TryRead(out _)){}
        lock(playbackLock){try{speaker?.Pause();speaker?.Flush();if(!closed)speaker?.Play();}catch{}}
    }
    public bool FirstToolCall(string id)=>connection?.FirstToolCall(id)==true;
    public bool CanCompleteTool(string id)=>!closed&&connection?.CanCompleteTool(id)==true;
    public void SetMuted(bool value){muted=value;if(value&&Connected)Send(new{realtimeInput=new{audioStreamEnd=true}});}
    public void SendFrame(string value){if(Connected)Send(new{realtimeInput=new{video=new{mimeType="image/jpeg",data=value}}});}
    public void Send(object value){if(Connected)_=SafeSend(value);}
    async Task SafeSend(object value){try{if(connection!=null)await connection.Send(value);}catch(OperationCanceledException){}catch(Exception e){if(!closed)Fail(e.Message);}}
    void Fail(string message)
    {
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(new{type="app.error",message},Brain.Json));
        onEvent(json.RootElement.Clone());Dispose();
    }
    public Task StopAsync()
    {
        lock(stopLock)
        {
            if(stopping!=null)return stopping;
            closed=true;connection?.Dispose();life.Cancel();playback.Writer.TryComplete();
            return stopping=Task.Run(CleanupAudio);
        }
    }
    void CleanupAudio()
    {
        lock(audioLifetime)
        {
            try{microphone?.Stop();}catch{}
            try{microphone?.Release();echo?.Release();noise?.Release();}catch{}
            microphone=null;echo=null;noise=null;
            Interrupt();
            lock(playbackLock){try{speaker?.Stop();speaker?.Release();}catch{}speaker=null;}
            if(manager!=null)
            {
                try
                {
                    if(OperatingSystem.IsAndroidVersionAtLeast(31))
                    {if(previousDevice!=null&&!manager.SetCommunicationDevice(previousDevice))manager.ClearCommunicationDevice();else if(previousDevice==null)manager.ClearCommunicationDevice();}
                    else manager.SpeakerphoneOn=previousSpeaker;
                    manager.Mode=previousMode;
                }catch{}
                manager=null;
            }
        }
    }
    public void Dispose()=>_=StopAsync();
}
