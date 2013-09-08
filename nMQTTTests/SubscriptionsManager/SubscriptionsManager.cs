/* 
 * nMQTT, a .Net MQTT v3 client implementation.
 * http://wiki.github.com/markallanson/nmqtt
 * 
 * Copyright (c) 2009 Mark Allanson (mark@markallanson.net)
 *
 * Licensed under the MIT License. You may not use this file except 
 * in compliance with the License. You may obtain a copy of the License at
 *
 *     http://www.opensource.org/licenses/mit-license.php
*/

using System;

using Nmqtt;
using Moq;
using Xunit;

namespace NmqttTests.SubscriptionsManager
{
    public class SubscriptionsManagerTests
    {
        [Fact]
        public void Ctor()
        {
            var chMock = new Mock<IMqttConnectionHandler>();
            chMock.Setup(x => x.RegisterForMessage(MqttMessageType.SubscribeAck, It.IsAny<Func<MqttMessage, bool>>()));

            Nmqtt.SubscriptionsManager subs = new Nmqtt.SubscriptionsManager(chMock.Object);

            chMock.VerifyAll();
        }

        [Fact(Skip="Pending subscriptions is currently incomplete.")]
        public void SubscriptionRequestCreatesPendingSubscription()
        {
            var chMock = new Mock<IMqttConnectionHandler>();

            string topic = "testtopic";
            MqttQos qos = MqttQos.AtMostOnce;

            // run and verify the mocks were called.
            Nmqtt.SubscriptionsManager subs = new Nmqtt.SubscriptionsManager(chMock.Object);
            subs.RegisterSubscription<AsciiPayloadConverter>(topic, qos);

            Assert.Equal<SubscriptionStatus>(SubscriptionStatus.Pending, subs.GetSubscriptionsStatus(topic));
        }

        [Fact]
        public void DisposeUnregistersMessageCallback()
        {
            var chMock = new Mock<IMqttConnectionHandler>();
            chMock.Setup((x) => x.RegisterForMessage(MqttMessageType.SubscribeAck, It.IsAny<Func<MqttMessage, bool>>()));
            chMock.Setup((x) => x.UnRegisterForMessage(MqttMessageType.SubscribeAck, It.IsAny<Func<MqttMessage, bool>>()));

            Nmqtt.SubscriptionsManager subMgr = new Nmqtt.SubscriptionsManager(chMock.Object);
            subMgr.Dispose();

            chMock.VerifyAll();
        }

        [Fact]
        public void SubscriptionRequestInvokesSend()
        {
            MqttSubscribeMessage subMsg = null;

            var chMock = new Mock<IMqttConnectionHandler>();
            // mock the call to register and save the callback for later.
            chMock.Setup((x) => x.RegisterForMessage(MqttMessageType.SubscribeAck, It.IsAny<Func<MqttMessage, bool>>()));
            // mock the call to Send(), which should occur when the subscription manager tries to subscribe
            chMock.Setup(x => x.SendMessage(It.IsAny<MqttSubscribeMessage>()))
                .Callback((MqttMessage msg) => subMsg = (MqttSubscribeMessage)msg);

            string topic = "testtopic";
            MqttQos qos = MqttQos.AtMostOnce;

            // run and verify the mocks were called.
            Nmqtt.SubscriptionsManager subs = new Nmqtt.SubscriptionsManager(chMock.Object);
            subs.RegisterSubscription<AsciiPayloadConverter>(topic, qos);
            chMock.VerifyAll();

            // now check the message generated by the subscription manager was good - ie contain the topic at the specified qos
            Assert.Contains(topic, subMsg.Payload.Subscriptions.Keys);
            Assert.Equal<MqttQos>(MqttQos.AtMostOnce, subMsg.Payload.Subscriptions[topic]);
        }

        [Fact]
        public void MultipleSubscriptionsForSamePendingThrowsException()
        {
            var chMock = new Mock<IMqttConnectionHandler>();
            // mock the call to register and save the callback for later.
            chMock.Setup((x) => x.RegisterForMessage(MqttMessageType.SubscribeAck, It.IsAny<Func<MqttMessage, bool>>()));

            string topic = "testtopic";
            MqttQos qos = MqttQos.AtMostOnce;

            // run and verify the mocks were called.
            Nmqtt.SubscriptionsManager subs = new Nmqtt.SubscriptionsManager(chMock.Object);
            subs.RegisterSubscription<AsciiPayloadConverter>(topic, qos);
            chMock.VerifyAll();

            Assert.Throws<ArgumentException>(() => subs.RegisterSubscription<AsciiPayloadConverter>(topic, qos));
        }

        [Fact]
        public void AcknowledgedSubscriptionRequestCreatesActiveSubscription()
        {
            Func<MqttMessage, bool> theCallback = null;
            MqttSubscribeMessage subMsg = null;

            var chMock = new Mock<IMqttConnectionHandler>();
            // mock the call to register and save the callback for later.
            chMock.Setup((x) => x.RegisterForMessage(MqttMessageType.SubscribeAck, It.IsAny<Func<MqttMessage, bool>>()))
                .Callback((MqttMessageType msgtype, Func<MqttMessage, bool> cb) => theCallback = cb);
            // mock the call to Send(), which should occur when the subscription manager tries to subscribe
            chMock.Setup(x => x.SendMessage(It.IsAny<MqttSubscribeMessage>()))
                .Callback((MqttMessage msg) => subMsg = (MqttSubscribeMessage)msg);

            string topic = "testtopic";
            MqttQos qos = MqttQos.AtMostOnce;

            // run and verify the mocks were called.
            Nmqtt.SubscriptionsManager subs = new Nmqtt.SubscriptionsManager(chMock.Object);
            short subid = subs.RegisterSubscription<AsciiPayloadConverter>(topic, qos);
            chMock.VerifyAll();

            // now check the message generated by the subscription manager was good - ie contain the topic at the specified qos
            Assert.Contains(topic, subMsg.Payload.Subscriptions.Keys);
            Assert.Equal<MqttQos>(MqttQos.AtMostOnce, subMsg.Payload.Subscriptions[topic]);

            // execute the callback that would normally be initiated by the connection handler when a sub ack message arrived.
            theCallback(new MqttSubscribeAckMessage().WithMessageIdentifier(subid).AddQosGrant(MqttQos.AtMostOnce));

            Assert.Equal<SubscriptionStatus>(SubscriptionStatus.Active, subs.GetSubscriptionsStatus(topic));
        }

        [Fact]
        public void MultipleSubscriptionsForAcknowledgedSubscriptionThrowsException()
        {
            Func<MqttMessage, bool> theCallback = null;
            MqttSubscribeMessage subMsg = null;

            var chMock = new Mock<IMqttConnectionHandler>();
            // mock the call to register and save the callback for later.
            chMock.Setup((x) => x.RegisterForMessage(MqttMessageType.SubscribeAck, It.IsAny<Func<MqttMessage, bool>>()))
                .Callback((MqttMessageType msgtype, Func<MqttMessage, bool> cb) => theCallback = cb);
            // mock the call to Send(), which should occur when the subscription manager tries to subscribe
            chMock.Setup(x => x.SendMessage(It.IsAny<MqttSubscribeMessage>()))
                .Callback((MqttMessage msg) => subMsg = (MqttSubscribeMessage)msg);

            string topic = "testtopic";
            MqttQos qos = MqttQos.AtMostOnce;

            // run and verify the mocks were called.
            Nmqtt.SubscriptionsManager subs = new Nmqtt.SubscriptionsManager(chMock.Object);
            short subid = subs.RegisterSubscription<AsciiPayloadConverter>(topic, qos);
            chMock.VerifyAll();

            // now check the message generated by the subscription manager was good - ie contain the topic at the specified qos
            Assert.Contains(topic, subMsg.Payload.Subscriptions.Keys);
            Assert.Equal<MqttQos>(MqttQos.AtMostOnce, subMsg.Payload.Subscriptions[topic]);

            // execute the callback that would normally be initiated by the connection handler when a sub ack message arrived.
            theCallback(new MqttSubscribeAckMessage().WithMessageIdentifier(subid).AddQosGrant(MqttQos.AtMostOnce));

            Assert.Equal<SubscriptionStatus>(SubscriptionStatus.Active, subs.GetSubscriptionsStatus(topic));

            // NOW THE IMPORTANT PART - Try and subscribe agin to the same topic. 
            Assert.Throws<ArgumentException>(() => subs.RegisterSubscription<AsciiPayloadConverter>(topic, qos));
        }


        [Fact]
        public void SubscriptionAckForNonPendingSubscriptionThrowsException()
        {
            Func<MqttMessage, bool> theCallback = null;
            MqttSubscribeMessage subMsg = null;

            var chMock = new Mock<IMqttConnectionHandler>();
            // mock the call to register and save the callback for later.
            chMock.Setup((x) => x.RegisterForMessage(MqttMessageType.SubscribeAck, It.IsAny<Func<MqttMessage, bool>>()))
                .Callback((MqttMessageType msgtype, Func<MqttMessage, bool> cb) => theCallback = cb);
            // mock the call to Send(), which should occur when the subscription manager tries to subscribe
            chMock.Setup(x => x.SendMessage(It.IsAny<MqttSubscribeMessage>()))
                .Callback((MqttMessage msg) => subMsg = (MqttSubscribeMessage)msg);

            string topic = "testtopic";
            MqttQos qos = MqttQos.AtMostOnce;

            // run and verify the mocks were called.
            Nmqtt.SubscriptionsManager subs = new Nmqtt.SubscriptionsManager(chMock.Object);
            short subid = subs.RegisterSubscription<AsciiPayloadConverter>(topic, qos);
            chMock.VerifyAll();

            // now check the message generated by the subscription manager was good - ie contain the topic at the specified qos
            Assert.Contains(topic, subMsg.Payload.Subscriptions.Keys);
            Assert.Equal<MqttQos>(MqttQos.AtMostOnce, subMsg.Payload.Subscriptions[topic]);

            // execute the callback with a bogus message identifier.
            Assert.Throws<ArgumentException>(() => theCallback(new MqttSubscribeAckMessage().WithMessageIdentifier(999).AddQosGrant(MqttQos.AtMostOnce)));
        }

        [Fact]
        public void GetSubscriptionWithValidTopicReturnsSubscription()
        {
            Func<MqttMessage, bool> theCallback = null;
            var chMock = new Mock<IMqttConnectionHandler>();
            chMock.Setup((x) => x.RegisterForMessage(MqttMessageType.SubscribeAck, It.IsAny<Func<MqttMessage, bool>>()))
                .Callback((MqttMessageType msgtype, Func<MqttMessage, bool> cb) => theCallback = cb);

            string topic = "testtopic";
            MqttQos qos = MqttQos.AtMostOnce;

            // run and verify the mocks were called.
            Nmqtt.SubscriptionsManager subs = new Nmqtt.SubscriptionsManager(chMock.Object);
            var subid = subs.RegisterSubscription<AsciiPayloadConverter>(topic, qos);

            // execute the callback that would normally be initiated by the connection handler when a sub ack message arrived.
            theCallback(new MqttSubscribeAckMessage().WithMessageIdentifier(subid).AddQosGrant(MqttQos.AtMostOnce));

            Assert.NotNull(subs.GetSubscription(topic));
        }

        [Fact]
        public void GetSubscriptionWithInvalidTopicReturnsNull()
        {
            Func<MqttMessage, bool> theCallback = null;

            var chMock = new Mock<IMqttConnectionHandler>();
            chMock.Setup((x) => x.RegisterForMessage(MqttMessageType.SubscribeAck, It.IsAny<Func<MqttMessage, bool>>()))
                .Callback((MqttMessageType msgtype, Func<MqttMessage, bool> cb) => theCallback = cb);

            string topic = "testtopic";
            MqttQos qos = MqttQos.AtMostOnce;

            // run and verify the mocks were called.
            Nmqtt.SubscriptionsManager subs = new Nmqtt.SubscriptionsManager(chMock.Object);
            var subid = subs.RegisterSubscription<AsciiPayloadConverter>(topic, qos);

            // execute the callback that would normally be initiated by the connection handler when a sub ack message arrived.
            theCallback(new MqttSubscribeAckMessage().WithMessageIdentifier(subid).AddQosGrant(MqttQos.AtMostOnce));

            Assert.Null(subs.GetSubscription("abc_badTopic"));
        }

        [Fact]
        public void GetSubscriptionForPendingSubscriptionReturnsNull()
        {
            var chMock = new Mock<IMqttConnectionHandler>();

            string topic = "testtopic";
            MqttQos qos = MqttQos.AtMostOnce;

            // run and verify the mocks were called.
            Nmqtt.SubscriptionsManager subs = new Nmqtt.SubscriptionsManager(chMock.Object);
            var subid = subs.RegisterSubscription<AsciiPayloadConverter>(topic, qos);

            Assert.Null(subs.GetSubscription(topic));
        }
    }
}
