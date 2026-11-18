using System;
using System.Collections.Generic;
using System.Linq;
using KageDock.Models;

namespace KageDock.Controllers
{

    internal sealed class KageDialogueReaction
    {
        public string Message { get; }
        public KageExpression Expression { get; }
        public KageExpressionEffect? Effect { get; }

        public KageDialogueReaction(
            string message,
            KageExpression expression,
            KageExpressionEffect? effect)
        {
            Message = message;
            Expression = expression;
            Effect = effect;
        }
    }

    internal sealed class KageDialogueController
    {
        private const int RecentMessageLimit = 10;
        private readonly Queue<string> _recentMessages = new();
        private readonly Dictionary<KageDialogueCategory, DateTime>
            _categoryCooldowns = new();
        private readonly Dictionary<KageDialogueCategory, TimeSpan>
        _categoryCooldownDurations = new();
        private static readonly Random Random = new();

        public KageDialogueReaction? CreateReaction(
            KageDialogueCategory category)
        {
            if (IsOnCooldown(category))
                return null;

            var messages = KageDialogueLibrary.GetMessages(category);

            if (messages.Count == 0)
                return null;

            var availableMessages = messages
                .Where(message => !_recentMessages.Contains(message))
                .ToList();

            // If every message was recently used, allow the full
            // collection again rather than getting stuck.
            if (availableMessages.Count == 0)
                availableMessages = messages.ToList();

            string selectedMessage =
                availableMessages[Random.Next(availableMessages.Count)];

            RememberMessage(selectedMessage);
            StartCooldown(category);

            return new KageDialogueReaction(
            selectedMessage,
            GetExpression(category, selectedMessage),
            GetEffect(category, selectedMessage));
        }

        public KageDialogueReaction? CreateWeightedReaction(
            params (KageDialogueCategory Category, int Weight)[] weightedCategories)
        {
            var availableCategories = weightedCategories
                .Where(item =>
                    item.Weight > 0 &&
                    !IsOnCooldown(item.Category) &&
                    KageDialogueLibrary.GetMessages(item.Category).Count > 0)
                .ToList();

            if (availableCategories.Count == 0)
                return null;

            int totalWeight = availableCategories.Sum(item => item.Weight);

            int roll = Random.Next(totalWeight);

            foreach (var item in availableCategories)
            {
                roll -= item.Weight;

                if (roll < 0)
                    return CreateReaction(item.Category);
            }

            return null;
        }

        public KageDialogueReaction? CreateCreatorSecretReaction(
            List<string> remainingMessages)
        {
            var messages = KageDialogueLibrary.GetMessages(
                KageDialogueCategory.CreatorSecret);

            if (messages.Count == 0)
                return null;

            // Remove messages that no longer exist in the library.
            remainingMessages.RemoveAll(message => !messages.Contains(message));

            // Refill only after the previous collection is exhausted.
            if (remainingMessages.Count == 0)
                remainingMessages.AddRange(messages);

            int index = Random.Next(remainingMessages.Count);

            string selectedMessage = remainingMessages[index];

            remainingMessages.RemoveAt(index);

            RememberMessage(selectedMessage);

            return new KageDialogueReaction(
                selectedMessage,
                KageExpression.Mischievous,
                null);
        }
        private bool IsOnCooldown(KageDialogueCategory category)
        {
            if (!_categoryCooldowns.TryGetValue(category, out var lastUsed))
                return false;

            if (!_categoryCooldownDurations.TryGetValue(category, out var cooldown))
                cooldown = GetCooldown(category);

            return DateTime.UtcNow - lastUsed < cooldown;
        }

        private void StartCooldown(KageDialogueCategory category)
        {
            TimeSpan cooldown = category == KageDialogueCategory.Idle
                ? TimeSpan.FromMinutes(Random.Next(5, 9))
                : GetCooldown(category);

            _categoryCooldownDurations[category] = cooldown;
            _categoryCooldowns[category] = DateTime.UtcNow;
        }

        private void RememberMessage(string message)
        {
            _recentMessages.Enqueue(message);

            while (_recentMessages.Count > RecentMessageLimit)
                _recentMessages.Dequeue();
        }

        private TimeSpan GetCooldown(KageDialogueCategory category)
        {
            return category switch
            {
                KageDialogueCategory.Idle => TimeSpan.FromMinutes(6),
                KageDialogueCategory.MusicIdle => TimeSpan.FromMinutes(8),

                KageDialogueCategory.MusicGeneral => TimeSpan.FromMinutes(4),
                KageDialogueCategory.MusicPause => TimeSpan.FromMinutes(3.5),
                KageDialogueCategory.MusicSkip => TimeSpan.FromMinutes(8),
                KageDialogueCategory.MusicApproval => TimeSpan.FromMinutes(5),
                KageDialogueCategory.MusicJudgment => TimeSpan.FromMinutes(5),

                KageDialogueCategory.Mischievous => TimeSpan.FromMinutes(1),
                KageDialogueCategory.Grumpy => TimeSpan.FromMinutes(1),
                KageDialogueCategory.Curious => TimeSpan.FromMinutes(1),
                KageDialogueCategory.Ridiculous => TimeSpan.FromMinutes(2),
                KageDialogueCategory.Affectionate => TimeSpan.FromMinutes(3),

                KageDialogueCategory.Kaze => TimeSpan.FromMinutes(8),
                KageDialogueCategory.Yozakura => TimeSpan.FromMinutes(8),
                KageDialogueCategory.Ember => TimeSpan.FromMinutes(8),
                KageDialogueCategory.Crimson => TimeSpan.FromMinutes(8),

                _ => TimeSpan.Zero
            };
        }
        
        private static KageExpression GetExpression(
            KageDialogueCategory category,
            string message)
        {
            return message switch
            {
                // MusicSkip — quiet observations
                "Oh, a change." or
                "Moving on, then." or
                "Something different." or
                "A change of pace." or
                "On to the next." or
                "Let's see." or
                "A different direction." or
                "Another possibility." or
                "Well, then." =>
                    KageExpression.HalfLidded,

                // MusicSkip — curiosity
                "What's next?" or
                "What's caught your attention?" or
                "I wonder what comes next." or
                "Trying something new?" or
                "Oh? Interesting." or
                "Let's hear this one." or
                "A different mood, perhaps." or
                "You have my curiosity." or
                "What's the atmosphere today?" or
                "Something caught your ear?" or
                "I'm listening." =>
                    KageExpression.Curious,

                // MusicSkip — subtle judgment
                "Not quite what you wanted?" or
                "Hmm. Fair enough." or
                "A matter of preference." or
                "Perhaps this one will do." or
                "Still exploring, I see." or
                "An interesting selection." or
                "I shall reserve judgment." or
                "You seem particular today." or
                "Fair enough. Next, then." or
                "I won't question it." =>
                    KageExpression.Judgmental,

                // MusicSkip — mischievous
                "Keeping me guessing?" or
                "Surprise me, then." or
                "Oh, we're exploring." or
                "Let's give this one a chance." or
                "I see what you're doing." or
                "A little variety never hurts." or
                "You do keep things interesting." or
                "I shall be watching." or
                "Another musical adventure." or
                "Well, this should be interesting." =>
                    KageExpression.Mischievous,

                // MusicPause — quiet observations
                "Oh. Silence." or
                "A moment of quiet." or
                "I'll enjoy the silence." or
                "Quite peaceful, actually." =>
                    KageExpression.HalfLidded,

                // MusicPause — curiosity
                "Taking a break?" or
                "Something on your mind?" or
                "I'll wait." =>
                    KageExpression.Curious,

                // MusicPause — subtle judgment
                "That's enough for now?" or
                "Finished listening?" or
                "We stopped, then." =>
                    KageExpression.SidewaysGlance,

                // Other specific dialogue
                "Already?" or
                "Another one? Seriously?" or
                "Commitment issues?" =>
                    KageExpression.Judgmental,

                "You really like this one." or
                "This again. Wonderful." =>
                    KageExpression.SidewaysGlance,

                "What is this?" or
                "You chose this voluntarily?" =>
                    KageExpression.Disappointed,

                "Oh?" or
                "What's that?" or
                "Something new?" =>
                    KageExpression.Curious,

                "Heh." or
                "I know what you're doing." =>
                    KageExpression.Mischievous,

                "Oh. You're back." or
                "What did I miss?" =>
                    KageExpression.Surprised,

                "Finally. A break." or
                "Five more minutes..." =>
                    KageExpression.Sleepy,

                _ => GetCategoryExpression(category)
            };
        }

        private static KageExpressionEffect? GetEffect(
            KageDialogueCategory category,
            string message)
        {
            // Exact dialogue reactions
            KageExpressionEffect? messageEffect = message switch
            {
                // Anger
                "Already?" or
                "Another one? Seriously?" or
                "Commitment issues?" or
                "Make up your mind." =>
                    KageExpressionEffect.Anger,

                // Question
                "What's that?" or
                "Oh?" or
                "Something new?" or
                "What's happening?" or
                "May I see?" or
                "What's the plan?" or
                "What's this about?" =>
                    KageExpressionEffect.Question,

                // Awkward
                "You really like this one." or
                "This again. Wonderful." =>
                    KageExpressionEffect.Awkward,

                // Sigh
                "What is this?" or
                "You chose this voluntarily?" or
                "I have concerns." or
                "I'm not impressed." =>
                    KageExpressionEffect.Sigh,

                // Sparkle
                "I approve." or
                "Now we're talking." or
                "You have taste after all." =>
                    KageExpressionEffect.Sparkle,

                // Exclamation
                "What did I just witness?" or
                "Absolutely not." or
                "I wasn't programmed for this." =>
                    KageExpressionEffect.Exclamation,

                // Ellipsis
                "..." or
                "Interesting..." or
                "I'm watching you." or
                "I was enjoying the silence." or
                "This one's been playing for a while." or
                "Keeping me guessing?" or
                "I see what you're doing." or
                "Wake me when it's interesting." or
                "Five more minutes..." or
                "Goodnight. I suppose." or
                "I wasn't sleeping." =>
                    KageExpressionEffect.Ellipsis,

                // Additional Idle reactions
                "Still here?" or
                "Anything happening down there?" =>
                    KageExpressionEffect.Question,

                "You seem busy doing nothing." or
                "Interesting use of time." =>
                    KageExpressionEffect.Sigh,

                // Additional MusicGeneral reactions
                "Interesting choice." =>
                    KageExpressionEffect.Question,

                "Well, that's unexpected." =>
                    KageExpressionEffect.Exclamation,

                "A questionable selection." =>
                    KageExpressionEffect.Sigh,

                // Additional MusicIdle reactions
                "Still listening?" =>
                    KageExpressionEffect.Question,

                "I suppose this is nice." =>
                    KageExpressionEffect.Sparkle,

                // Additional MusicSkip reactions
                "What's caught your attention?" or
                "I wonder what comes next." or
                "Oh? Interesting." =>
                    KageExpressionEffect.Question,

                "Not quite what you wanted?" =>
                    KageExpressionEffect.Sigh,

                "Surprise me, then." =>
                    KageExpressionEffect.Exclamation,

                // Additional Sleep reactions
                "Finally. A break." =>
                    KageExpressionEffect.Sigh,

                // Additional Wake reactions
                "Oh. You're back." or
                "What did I miss?" =>
                    KageExpressionEffect.Question,

                "Already? I was comfortable." or
                "Took you long enough." =>
                    KageExpressionEffect.Sigh,

                _ => null
            };

            if (messageEffect.HasValue)
                return messageEffect;

            // Category defaults.
            return category switch
            {
                KageDialogueCategory.Curious =>
                    KageExpressionEffect.Question,

                KageDialogueCategory.Grumpy =>
                    KageExpressionEffect.Anger,

                KageDialogueCategory.Mischievous =>
                    KageExpressionEffect.Ellipsis,

                KageDialogueCategory.Ridiculous =>
                    KageExpressionEffect.Exclamation,

                KageDialogueCategory.Affectionate =>
                    KageExpressionEffect.Sparkle,

                KageDialogueCategory.MusicApproval =>
                    KageExpressionEffect.Sparkle,

                KageDialogueCategory.MusicJudgment =>
                    KageExpressionEffect.Sigh,

                KageDialogueCategory.MusicPause =>
                    KageExpressionEffect.Ellipsis,

                KageDialogueCategory.CreatorSecret =>
                    KageExpressionEffect.Question,

                // Keep frequent or quiet reactions unobstructed.
                KageDialogueCategory.Idle or
                KageDialogueCategory.MusicGeneral or
                KageDialogueCategory.MusicIdle or
                KageDialogueCategory.MusicSkip or
                KageDialogueCategory.Sleep or
                KageDialogueCategory.Wake =>
                    null,

                _ => null
            };
        }
        private static KageExpression GetCategoryExpression(
            KageDialogueCategory category)
        {
            return category switch
            {
                KageDialogueCategory.MusicPause =>
                    KageExpression.HalfLidded,
                    
                KageDialogueCategory.MusicSkip =>
                    KageExpression.Judgmental,

                KageDialogueCategory.MusicJudgment =>
                    KageExpression.Disappointed,

                KageDialogueCategory.Curious =>
                    KageExpression.Curious,

                KageDialogueCategory.Mischievous =>
                    KageExpression.Mischievous,

                KageDialogueCategory.Sleep =>
                    KageExpression.Sleepy,

                KageDialogueCategory.Ridiculous =>
                    KageExpression.HalfLidded,

                _ => KageExpression.Neutral
            };
        }
    }
}
