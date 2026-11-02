<script setup lang="ts">
import type { AppError } from '@wow-two-beta/fe/foundation/results';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Card } from '@wow-two-beta/fe/presentation/display';
import { onMounted, onScopeDispose, shallowRef, watch } from 'vue';
import type { OrganizationMemberDto } from '../../../../contracts';
import { useAccountText } from '../../../../kit/account/hooks';
import { FailureAlert } from '../../../../kit/presentation/feedback/failureAlert';
import type { OrganizationClient } from '../../../organizations';
import { useOrganizationClient } from '../../../organizations/hooks';
import MemberRow from './MemberRow.vue';

/**
 * Lists an organization's members with their roles for its owners and admins: re-roles a member through the role
 * catalog, `owner` included, removes one after a confirm step, and approves or denies a member's pending Telegram account
 * change, shown as "Telegram account change pending". A server refusal shows under the member's row.
 */
defineOptions({ name: 'MembersPanel' });

const props = withDefaults(
  defineProps<{
    /** The organization whose members the panel manages. */
    organizationId: string;
    /** The organization API to call; built from `baseUrl` when omitted. */
    client?: OrganizationClient;
    /** The service's origin; the consumer's own origin when omitted. */
    baseUrl?: string;
  }>(),
  { client: undefined, baseUrl: undefined },
);

const emit = defineEmits<{
  /** Fires once a member holds a new role. */
  roleChanged: [member: OrganizationMemberDto];
  /** Fires once a member is out of the organization. */
  removed: [userId: string];
  /** Fires once a member's pending Telegram account change is approved (`true`) or denied (`false`). */
  telegramChangeResolved: [userId: string, isApproved: boolean];
}>();

const client = useOrganizationClient(props);
const t = useAccountText();
const isLoading = shallowRef(true);
const members = shallowRef<ReadonlyArray<OrganizationMemberDto>>([]);
const failure = shallowRef<AppError | null>(null);
let controller: AbortController | null = null;

/** Loads the members, dropping an answer a newer load replaced. */
async function load() {
  controller?.abort();
  const current = new AbortController();
  controller = current;
  isLoading.value = true;
  const result = await client.listMembers(props.organizationId, current.signal);
  if (current.signal.aborted) return;
  isLoading.value = false;
  members.value = result.ok ? result.value : [];
  failure.value = result.ok ? null : result.failure;
}

function changed(userId: string, role: string) {
  members.value = members.value.map((member) => (member.userId === userId ? { ...member, role } : member));
  const member = members.value.find((candidate) => candidate.userId === userId);
  if (member) emit('roleChanged', member);
}

function telegramChangeResolved(userId: string, isApproved: boolean) {
  members.value = members.value.map((member) =>
    member.userId === userId ? { ...member, hasPendingTelegramChange: false } : member,
  );
  emit('telegramChangeResolved', userId, isApproved);
}

function removed(userId: string) {
  members.value = members.value.filter((member) => member.userId !== userId);
  emit('removed', userId);
}

onMounted(load);
watch(() => props.organizationId, load);
onScopeDispose(() => controller?.abort());

defineExpose({
  /** Loads the members again. */
  reload: load,
});
</script>

<template>
  <Card class="flex flex-col gap-3 p-6">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <h2 class="text-base font-semibold">{{ t('MembersPanel.title', 'Members') }}</h2>
      <Button type="button" variant="outline" tone="neutral" size="sm" :is-loading="isLoading" @click="load">
        {{ t('MembersPanel.reload', 'Reload') }}
      </Button>
    </div>
    <FailureAlert :failure="failure" />
    <ul v-if="members.length" class="divide-y divide-border" :aria-label="t('MembersPanel.title', 'Members')">
      <MemberRow
        v-for="member in members"
        :key="member.userId"
        :member="member"
        :organization-id="organizationId"
        :client="client"
        @role-changed="changed(member.userId, $event)"
        @removed="removed(member.userId)"
        @telegram-change-resolved="telegramChangeResolved(member.userId, $event)"
      />
    </ul>
    <p v-else-if="!isLoading && !failure" class="text-sm text-muted-foreground">
      {{ t('MembersPanel.empty', 'No members yet.') }}
    </p>
  </Card>
</template>
