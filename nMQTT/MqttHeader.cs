﻿/* 
 * nMQTT, a .Net MQTT v3 client implementation.
 * http://code.google.com/p/nmqtt
 * 
 * Copyright (c) 2009 Mark Allanson (mark@markallanson.net)
 *
 * Licensed under the MIT License. You may not use this file except 
 * in compliance with the License. You may obtain a copy of the License at
 *
 *     http://www.opensource.org/licenses/mit-license.php
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Nmqtt
{
    /// <summary>
    /// Represents the Fixed Header of an MQTT message.
    /// </summary>
    public partial class MqttHeader
    {
        /// <summary>
        /// Backing storage for the payload size.
        /// </summary>
        private int messageSize;
        
        /// <summary>
        /// Gets or sets the type of the MQTT message.
        /// </summary>
        /// <value>The type of the MQTT message.</value>
        public MqttMessageType MessageType { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this MQTT Message is duplicate of a previous message.
        /// </summary>
        /// <value><c>true</c> if duplicate; otherwise, <c>false</c>.</value>
        public bool Duplicate { get; set; }

        /// <summary>
        /// Gets or sets the Quality of Service indicator for the message.
        /// </summary>
        /// <value>The qos.</value>
        public MqttQos Qos { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this MQTT message should be retained by the message broker for transmission to new subscribers.
        /// </summary>
        /// <value><c>true</c> if message should be retained by the message broker; otherwise, <c>false</c>.</value>
        public bool Retain { get; set; }

        /// <summary>
        /// Gets or sets the size of the variable header + payload section of the message.
        /// </summary>
        /// <value>The size of the variable header + payload.</value>
        /// <exception cref="nMQQT.InvalidPayloadSizeException">The size of the variable header + payload exceeds the maximum allowed size.</exception>
        public int MessageSize
        {
            get
            {
                return this.messageSize;
            }
            set
            {
                if (value < 0 || value > Constants.MaxMessageSize)
                {
                    throw new InvalidPayloadSizeException(value, Constants.MaxMessageSize);
                }

                messageSize = value;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MqttHeader"/> class.
        /// </summary>
        public MqttHeader()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MqttHeader"/> class based on data contained within the supplied stream.
        /// </summary>
        /// <param name="stream">The stream containing the header message.</param>
        public MqttHeader(Stream headerStream)
        {
            ReadFrom(headerStream);
        }

        /// <summary>
        /// Writes the header to a supplied stream.
        /// </summary>
        /// <param name="messageStream">The stream to write the header bytes to.</param>
        public void WriteTo(Stream messageStream)
        {
            List<byte> headerBytes = HeaderBytes;
            messageStream.Write(headerBytes.ToArray(), 0, headerBytes.Count);
        }

        /// <summary>
        /// Creates a new MqttHeader based on a list of bytes.
        /// </summary>
        /// <param name="headerStream">The stream that contains the message, positioned at the beginning of the header.</param>
        /// <returns></returns>
        internal void ReadFrom(Stream headerStream)
        {
            if (headerStream.Length < 2)
            {
                throw new InvalidHeaderException("The supplied header is invalid. Header must be at least 2 bytes long.");
            }

            int firstHeaderByte = headerStream.ReadByte();
            // pull out the first byte
            Retain = ((firstHeaderByte & 1) == 1 ? true : false);
            Qos = (MqttQos)((firstHeaderByte & 6) >> 1);
            Duplicate = (((firstHeaderByte & 8) >> 3) == 1 ? true : false);
            MessageType = (MqttMessageType)((firstHeaderByte & 240) >> 4);

            try
            {
                // decode the remaining bytes as the remaining/payload size, input param is the 2nd to last byte of the header byte list
                MessageSize = ReadRemainingLength(headerStream);
            }
            catch (InvalidPayloadSizeException ex)
            {
                throw new InvalidHeaderException("The header being processed contained an invalid size byte pattern." +
                    "Message size must take a most 4 bytes, and the last byte must have bit 8 set to 0.", ex);
            }
        }

        /// <summary>
        /// Gets the value of the Mqtt header as a byte array
        /// </summary>
        private List<byte> HeaderBytes
        {
            get
            {
                var headerBytes = new List<byte>();

                // build the bytes that make up the header. The first byte is a combination of message type, dup,
                // qos and retain, and the follow bytes (up to 4 of them) are the size of the payload + variable header.
                headerBytes.Add((byte)((((int)MessageType) << 4) + ((Duplicate ? 1 : 0) << 3) + (((int)Qos) << 1) + (Retain ? 1 : 0)));
                headerBytes.AddRange(GetRemainingLengthBytes());
                return headerBytes;
            }
        }

        private static int ReadRemainingLength(Stream headerStream)
        {
            var remainingLength = 0;
            var multiplier = 1;
            var currentByteCount = 0;
            var currentByte = 0;

            // keep going while the last bit of the header bytes is zero, except if the 4th message size header
            // byte has a 1 on the last bit, in which case the message is invalid (according to the spec)
            // so throw it out because we really wouldn't have a clue what the message size actually is.
            do
            {
                if (currentByteCount++ == 4)
                {
                    // we dont know the real payload size, so just report it as -1.
                    throw new InvalidPayloadSizeException(-1, Constants.MaxMessageSize);
                }

                // read the next byte, but if this means we reach the end of the stream, then this is a problem
                // becuase we haven't yet read all of the header - the message in the stream is corrupt
                currentByte = headerStream.ReadByte();
                if (currentByte == -1)
                {
                    throw new InvalidHeaderException("The header was not complete (too short). The message is malformed.");
                }

                remainingLength += (currentByte & 127) * multiplier;
                multiplier *= 128;
            } while ((currentByte & 128) != 0);

            return remainingLength;
        }

        /// <summary>
        /// Calculates and return the bytes that represent the remaining length of the message.
        /// </summary>
        /// <returns></returns>
        private List<byte> GetRemainingLengthBytes()
        {
            var lengthBytes = new List<byte>();
            int payloadCalc = messageSize;
            
            // generate a byte array based on the message size, splitting it up into
            // 7 bit chunks, with the 8th bit being used to indicate "one more to come"
            do
            {
                int nextByteValue = payloadCalc % 128;
                payloadCalc = payloadCalc / 128;
                if (payloadCalc > 0)
                {
                    nextByteValue = nextByteValue | 0x80;
                }
                lengthBytes.Add((byte)nextByteValue);
            } while (payloadCalc > 0);

            return lengthBytes;
        }

        /// <summary>
        /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine(String.Format("Header: MessageType={0}, Duplicate={1}, Retain={2}, Qos={3}, Size={4}",
                MessageType, Duplicate, Retain, Qos, MessageSize));

            return sb.ToString();
        }
    }
}