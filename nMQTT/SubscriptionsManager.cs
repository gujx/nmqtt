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
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Nmqtt
{
    internal class SubscriptionsManager : IDisposable
    {
        /// <summary>
        ///     List of confirmed subscriptions, keyed on the topic name.
        /// </summary>
        private readonly Dictionary<string, Subscription> subscriptions = new Dictionary<string, Subscription>();

        /// <summary>
        ///     A list of subscriptions that are pending acknowledgement, keyed on the message identifier.
        /// </summary>
        private readonly Dictionary<int, Subscription> pendingSubscriptions = new Dictionary<int, Subscription>();

        private readonly IMqttConnectionHandler connectionHandler;

        public SubscriptionsManager(IMqttConnectionHandler connectionHandler) {
            this.connectionHandler = connectionHandler;
            this.connectionHandler.RegisterForMessage(MqttMessageType.SubscribeAck, ConfirmSubscription);
        }

        /// <summary>
        ///     Registers a new subscription with the subscription manager.
        /// </summary>
        /// <param name="topic"></param>
        /// <param name="qos"></param>
        /// <returns>The subscription message identifier.</returns>
        internal IObservable<MqttReceivedMessage<T>> RegisterSubscription<T, TPayloadConverter>(string topic, MqttQos qos)
            where TPayloadConverter : IPayloadConverter<T>, new() {
            // check we don't have a pending subscription request for the topic.
            var pendingSubs = from ps in pendingSubscriptions.Values
                              where ps.Topic.Equals(topic)
                              select ps;
            if (pendingSubs.Any()) {
                throw new ArgumentException("There is already a pending subscription for this topic");
            }

            // no pending subscription, if we already have a subscription then throw it back out as well
            if (subscriptions.ContainsKey(topic)) {
                // TODO: we might want to treat this as an ignore/silent confirm because they will be receiving messages for the topic already
                throw new ArgumentException("You are already subscribed for this topic");
            }

            var payloadConverter = new TPayloadConverter();
            var subject = new Subject<byte[]>();
            var observable = subject.Select(ba => new MqttReceivedMessage<T>(topic, payloadConverter.ConvertFromBytes(ba)));

            // Add a pending subscription...
            var sub = new Subscription<T> {
                Topic = topic,
                Qos = qos,
                MessageIdentifier = MessageIdentifierDispenser.GetNextMessageIdentifier("subscriptions"),
                CreatedTime = DateTime.Now,
                Subject = subject,
                Observable = observable,
            };

            pendingSubscriptions.Add(sub.MessageIdentifier, sub);

            // build a subscribe message for the caller.
            MqttSubscribeMessage msg = new MqttSubscribeMessage()
                .WithMessageIdentifier(sub.MessageIdentifier)
                .ToTopic(sub.Topic)
                .AtQos(sub.Qos);

            connectionHandler.SendMessage(msg);

            return sub.Observable;
        }

        /// <summary>
        ///     Action delegate method that initates the unsubscribe process for a topic on the broker when there are no more subscribers.
        /// </summary>
        /// <param name="topic">The topic to clean.</param>
        /// <param name="observer"></param>
        public void SubscriptionCleaner<T>(string topic, IObserver<T> observer) {

        }

        /// <summary>
        ///     Confirms a subscription has been made with the broker. Marks the sub as confirmed in the subs storage.
        /// </summary>
        /// <param name="msg">The message that triggered subscription confirmation.</param>
        private bool ConfirmSubscription(MqttMessage msg) {
            var subAck = (MqttSubscribeAckMessage) msg;

            Subscription sub;
            if (!pendingSubscriptions.TryGetValue(subAck.VariableHeader.MessageIdentifier, out sub)) {
                throw new ArgumentException(
                    String.Format("There is no pending subscription against message identifier {0}",
                                  subAck.VariableHeader.MessageIdentifier));
            }

            // move it to the subscriptions pool, and out of the pending pool.
            subscriptions.Add(sub.Topic, sub);
            pendingSubscriptions.Remove(subAck.VariableHeader.MessageIdentifier);

            return true;
        }

        /// <summary>
        ///     Gets the current status of a subscription
        /// </summary>
        /// <param name="topic">The topic to check the subscription for.</param>
        /// <returns>The current status of the subscription</returns>
        public SubscriptionStatus GetSubscriptionsStatus(string topic) {
            var status = SubscriptionStatus.DoesNotExist;

            // if its live, return active
            if (subscriptions.ContainsKey(topic)) {
                status = SubscriptionStatus.Active;
            }


            pendingSubscriptions.SingleOrDefault<KeyValuePair<int, Subscription>>(
                pair => pair.Value.Topic.Equals(topic, StringComparison.Ordinal));

            // if its pending, return pending.
            //if (pendingSubscriptions.SingleOrDefault<KeyValuePair<int, Subscription>>(pair => pair.Value.Topic.Equals(topic, StringComparison.Ordinal)) != null)
            //{
            //    status = SubscriptionStatus.Pending;
            // }

            return status;
        }

        /// <summary>
        ///     Gets the subscription data method registered for a subscription topic.
        /// </summary>
        /// <param name="topic">The topic to retrieve the subscription data for.</param>
        /// <returns>The subscription data for a subscription, or null if there is no registered subscription.</returns>
        /// <remarks>
        ///     This will ignore pending subscriptions, so any messages that arrive for pending subscriptions will NOT be delivered. This
        ///     policy may change in the future if I find that some brokers might be a bit shifty. Sending messages to callbacks that
        ///     are not yet confirmed might not be handled gracefully by client consumers.
        /// </remarks>
        public Subscription GetSubscription(string topic) {
            Subscription subs;
            if (!subscriptions.TryGetValue(topic, out subs)) {
                return null;
            }
            return subs;
        }

        /// <summary>
        ///     Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose() {
            if (connectionHandler != null) {
                connectionHandler.UnRegisterForMessage(MqttMessageType.SubscribeAck, ConfirmSubscription);
            }

            GC.SuppressFinalize(this);
        }
    }
}