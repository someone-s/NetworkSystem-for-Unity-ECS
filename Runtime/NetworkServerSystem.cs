using UnityEngine;

using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Error;

namespace Eden.Network
{
    [DisableAutoCreation]
    public class NetworkServerSystem : SystemBase
    {
        private NetworkDriver driver;
        private NativeList<NetworkConnection> connections;

        private FixedString32 password;
        private uint newIndex;
        private NativeList<uint> reusableIndexes;

        private SimulationSystemGroup group;

        public void ConnectAll(ushort port, FixedString32 password)
        {
            DisconnectAll();

            this.password = password;
            newIndex = 0;
            reusableIndexes = new NativeList<uint>(10, Allocator.Persistent);

            driver = NetworkDriver.Create();
            connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);

            var endpoint = NetworkEndPoint.AnyIpv4;
            endpoint.Port = port;

            driver.Bind(endpoint);
            driver.Listen();

            Enabled = true;
        }
        public void DisconnectAll()
        {
            if (driver.IsCreated && connections.IsCreated)
                foreach(NetworkConnection connection in connections)
                    driver.Disconnect(connection);

            Close();
        }
        public void Close()
        {
            if (reusableIndexes.IsCreated)
                reusableIndexes.Dispose();
            if (driver.IsCreated)
                driver.Dispose();
            if (connections.IsCreated)
                connections.Dispose();

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

            Close();
        }
        protected override void OnUpdate()
        {
            driver.ScheduleUpdate().Complete();

            UpdateConnections();

            for (int i = 0; i < connections.Length; i++)
                ProcessConnection(i);
        }

        private void UpdateConnections()
        {
            for (int i = 0; i < connections.Length; i++)
                if (!connections[i].IsCreated)
                {
                    connections.RemoveAtSwapBack(i);
                    --i;
                }

            NetworkConnection c;
            while ((c = driver.Accept()) != default(NetworkConnection))
            {
                connections.Add(c);
            }
        }
        private void ProcessConnection(int i)
        {
            DataStreamReader stream;
            NetworkEvent.Type type;
            while ((type = driver.PopEventForConnection(connections[i], out stream)) != NetworkEvent.Type.Empty)
                ProcessEvent(i, type, ref stream);
        }
        private void ProcessEvent(int i, NetworkEvent.Type type, ref DataStreamReader stream)
        {
            if (type == NetworkEvent.Type.Disconnect)
            {;
                Debug.Log($"Server: Client disconnetced from server, {stream.ReadByte()}");

                connections[i] = default(NetworkConnection);
            }
            else if (type == NetworkEvent.Type.Data)
            {
                if (stream.ReadFixedString32() != password)
                {
                    driver.Disconnect(connections[i]);
                    return;
                }

                switch(stream.ReadByte())
                {
                    case NetworkCodes.Pinging:
                        AcknowledgeReportIn(i);
                        break;
                    case NetworkCodes.Add:
                        ForwardAdd(ref stream);
                        break;
                    case NetworkCodes.Modify:
                        ForwardModify(i, ref stream);
                        break;
                    case NetworkCodes.Delete:
                        ForwardDelete(i, ref stream);
                        break;
                    case NetworkCodes.Present:
#if UNITY_EDITOR
                        Debug.Log("Server: Present Recieved");
#endif
                        break;
                    default:
#if UNITY_EDITOR
                        Debug.Log("Server: Unknown Instruction");
#endif
                        break;
                }
            }
        }

        private void AcknowledgeReportIn(int i)
        {
#if UNITY_EDITOR
            Debug.Log("Server: Ping Recieved");
#endif

            driver.BeginSend(connections[i], out DataStreamWriter writer);
            writer.WriteFixedString32(password);
            writer.WriteByte(NetworkCodes.Recieved);
            driver.EndSend(writer);
        }
        private void ForwardAdd(ref DataStreamReader stream)
        {
            uint index;
            if (reusableIndexes.Length > 0)
            {
                index = reusableIndexes[0];
                reusableIndexes.RemoveAt(0);
            }
            else
            {
                index = newIndex;
                newIndex++;
            }

            float3 position = new float3(stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat());
            quaternion rotation = new quaternion(stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat());
            float scale = stream.ReadFloat();

            FixedString4096 modName = stream.ReadFixedString4096();

            for (int k = 0; k < connections.Length; k++)
            {
                driver.BeginSend(connections[k], out DataStreamWriter writer);
                writer.WriteFixedString32(password);
                writer.WriteByte(NetworkCodes.Add);
                writer.WriteUInt(index);
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
            }
        }
        private void ForwardModify(int i, ref DataStreamReader stream)
        {
            uint index = stream.ReadUInt();

            float3 position = new float3(stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat());
            quaternion rotation = new quaternion(stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat());
            float scale = stream.ReadFloat();

            for (int k = 0; k < connections.Length; k++)
            {
                if (k == i) continue;

                driver.BeginSend(connections[k], out DataStreamWriter writer);
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
            }
        }
        private void ForwardDelete(int i, ref DataStreamReader stream)
        {
            uint index = stream.ReadUInt();
            reusableIndexes.Add(index);

            for (int k = 0; k < connections.Length; k++)
            {
                if (k == i) continue;

                driver.BeginSend(connections[k], out DataStreamWriter writer);
                writer.WriteFixedString32(password);
                writer.WriteByte(NetworkCodes.Delete);
                writer.WriteUInt(index);
                driver.EndSend(writer);
            }
        }
    }
}

