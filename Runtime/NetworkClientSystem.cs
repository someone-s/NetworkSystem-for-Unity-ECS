using System;
using UnityEngine;

using Unity.Entities;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Mathematics;

namespace Eden.Network
{
    [DisableAutoCreation]
    [UpdateAfter(typeof(NetworkServerSystem))]
    public class NetworkClientSystem : SystemBase
    {
        public event Action<uint, float3, quaternion, float, FixedString4096> AddEvent;
        public event Action<uint, float3, quaternion, float> ModifyEvent;
        public event Action<uint> DeleteEvent;

        private NetworkDriver driver;
        private NetworkConnection connection;

        private NetworkEndPoint endPoint;
        private FixedString32 password;

        private int sinceLastSendMS = 0;
        private float timeoutMS = 750;

        private SimulationSystemGroup group;

        public void Connect(NetworkEndPoint endPoint, FixedString32 password)
        {
            Disconnect();

            this.endPoint = endPoint;
            this.password = password;

            driver = NetworkDriver.Create();
            connection = default(NetworkConnection);
            connection = driver.Connect(this.endPoint);

            Enabled = true;
        }
        public void Disconnect()
        {
            if (driver.IsCreated && connection.IsCreated)
                driver.Disconnect(connection);

            Close();
        }
        public void Close()
        {
            if (driver.IsCreated) driver.Dispose();

            Enabled = false;
        }
        protected override void OnCreate()
        {
            group = World.GetOrCreateSystem<SimulationSystemGroup>();
            group.AddSystemToUpdateList(this);

            Enabled = false;
        }
        protected override void OnDestroy()
        {
            group.RemoveSystemFromUpdateList(this);

            Disconnect();
        }
        protected override void OnUpdate()
        {
            sinceLastSendMS += (int)(Time.DeltaTime * 1000f);

            driver.ScheduleUpdate().Complete();

            DataStreamReader stream;
            NetworkEvent.Type type;
            while (Enabled == true && (type = driver.PopEventForConnection(connection, out stream)) != NetworkEvent.Type.Empty)
                ProcessEvent(type, ref stream);

            if (sinceLastSendMS > timeoutMS)
            {
                driver.BeginSend(connection, out DataStreamWriter writer);
                writer.WriteFixedString32(password);
                writer.WriteByte(NetworkCodes.Present);
                driver.EndSend(writer);

                sinceLastSendMS = 0;
            }
        }

        private void ProcessEvent(NetworkEvent.Type type, ref DataStreamReader stream)
        {
            if (type == NetworkEvent.Type.Connect)
            {
#if UNITY_EDITOR
                Debug.Log("Client: Pinging to server");
#endif
                driver.BeginSend(connection, out DataStreamWriter writer);
                writer.WriteFixedString32(password);
                writer.WriteByte(NetworkCodes.Pinging);
                driver.EndSend(writer);

                sinceLastSendMS = 0;
            }
            else if (type == NetworkEvent.Type.Disconnect)
            {
#if UNITY_EDITOR
                Debug.Log($"Client: Disconnected from server, {stream.ReadByte()}");
#endif
                Close();
            }
            else if (type == NetworkEvent.Type.Data)
            {
                if (stream.ReadFixedString32() != password)
                    return;

                switch (stream.ReadByte())
                {
                    case NetworkCodes.Recieved:
#if UNITY_EDITOR
                        Debug.Log("Client: PingIn success");
#endif
                        break;
                    case NetworkCodes.Add:
                        ReadAdd(ref stream);
                        break;
                    case NetworkCodes.Modify:
                        ReaddModify(ref stream);
                        break;
                    case NetworkCodes.Delete:
                        ReadDelete(ref stream);
                        break;
                    default:
#if UNITY_EDITOR
                        Debug.Log("Client: Unknown Instruction");
#endif
                        break;
                }
            }
        }

        private void ReadAdd(ref DataStreamReader stream)
        {
            uint index = stream.ReadUInt();

            float3 position = new float3(stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat());
            quaternion rotation = new quaternion(stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat());
            float scale = stream.ReadFloat();

            FixedString4096 t = stream.ReadFixedString4096();
            string modName = t.ConvertToString();

            AddEvent.Invoke(index, position, rotation, scale, modName);
        }
        private void ReaddModify(ref DataStreamReader stream)
        {
            uint index = stream.ReadUInt();

            float3 position = new float3(stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat());
            quaternion rotation = new quaternion(stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat());
            float scale = stream.ReadFloat();

            ModifyEvent.Invoke(index, position, rotation, scale);
        }
        private void ReadDelete(ref DataStreamReader stream)
        {
            uint index = stream.ReadUInt();

            DeleteEvent.Invoke(index);
        }

        public void WriteAdd(float3 position, quaternion rotation, float scale, FixedString4096 modName)
        {
            driver.BeginSend(connection, out DataStreamWriter writer);
            writer.WriteFixedString32(password);
            writer.WriteByte(NetworkCodes.Add);
            writer.WriteFloat(position.x);
            writer.WriteFloat(position.y);
            writer.WriteFloat(position.z);
            writer.WriteFloat(rotation.value.x);
            writer.WriteFloat(rotation.value.y);
            writer.WriteFloat(rotation.value.z);
            writer.WriteFloat(rotation.value.w);
            writer.WriteFloat(scale);
            writer.WriteFixedString4096(modName);
            driver.EndSend(writer);

            sinceLastSendMS = 0;
        }
        public void WriteModify(uint index, float3 position, quaternion rotation, float scale)
        {
            driver.BeginSend(connection, out DataStreamWriter writer);
            writer.WriteFixedString32(password);
            writer.WriteByte(NetworkCodes.Modify);
            writer.WriteUInt(index);
            writer.WriteFloat(position.x);
            writer.WriteFloat(position.y);
            writer.WriteFloat(position.z);
            writer.WriteFloat(rotation.value.x);
            writer.WriteFloat(rotation.value.y);
            writer.WriteFloat(rotation.value.z);
            writer.WriteFloat(rotation.value.w);
            writer.WriteFloat(scale);
            driver.EndSend(writer);

            sinceLastSendMS = 0;
        }
        public void WriteDelete(uint index)
        {
            driver.BeginSend(connection, out DataStreamWriter writer);
            writer.WriteFixedString32(password);
            writer.WriteByte(NetworkCodes.Delete);
            writer.WriteUInt(index);
            driver.EndSend(writer);

            sinceLastSendMS = 0;
        }
    }
}
