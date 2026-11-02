<script setup lang="ts">
import type { AppError } from '@wow-two-beta/fe/foundation/results';
import { useLocaleFormatters } from '@wow-two-beta/fe/foundation/i18n';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Badge, BadgeVariant } from '@wow-two-beta/fe/presentation/display';
import { computed, shallowRef } from 'vue';
import type { InvitationDto, InvitationStatus } from '../../../../contracts';
import { useAccountText } from '../../../../kit/account/hooks';
import { FailureAlert } from '../../../../kit/presentation/feedback/failureAlert';
import type { OrganizationClient } from '../../../organizations';
import { invitationStatusLabel, roleLabel } from '../../../organizations/OrganizationLabels';

/** One invitation of the invitations panel: its role, who may redeem it and where it stands; revokes a pending one. */
defineOptions({ name: 'InvitationRow' });

const props = defineProps<{
  /** The invitation the row shows. */
  invitation: InvitationDto;
  /** The organization the invitation is into. */
  organizationId: string;
  /** The organization API to call. */
  client: OrganizationClient;
}>();

const emit = defineEmits<{
  /** Fires once the invitation is withdrawn. */
  revoked: [];
}>();

const statusVariants: Readonly<Record<InvitationStatus, BadgeVariant>> = {
  pending: BadgeVariant.Info,
  accepted: BadgeVariant.Success,
  expired: BadgeVariant.Neutral,
  revoked: BadgeVariant.Neutral,
};

const t = useAccountText();
const formatters = useLocaleFormatters();
const isRevoking = shallowRef(false);
const failure = shallowRef<AppError | null>(null);

const date = (value: string | null) => {
  const at = value ? Date.parse(value) : Number.NaN;
  return Number.isNaN(at) ? '' : formatters.value.date(at, { dateStyle: 'medium' });
};
const recipient = computed(
  () => props.invitation.phoneNumber || props.invitation.email || t('InvitationsPanel.anyone', 'Anyone with the link'),
);
const when = computed(() => {
  const { status, expiresAt, acceptedAt } = props.invitation;
  if (status === 'pending' && date(expiresAt)) {
    return t('InvitationsPanel.expires', 'Expires {date}', { date: date(expiresAt) });
  }
  if (status === 'accepted' && date(acceptedAt)) {
    return t('InvitationsPanel.accepted', 'Accepted {date}', { date: date(acceptedAt) });
  }
  return '';
});

async function revoke() {
  if (isRevoking.value) return;
  isRevoking.value = true;
  failure.value = null;
  const result = await props.client.revokeInvitation(props.organizationId, props.invitation.id);
  isRevoking.value = false;
  if (result.ok) emit('revoked');
  else failure.value = result.failure;
}
</script>

<template>
  <li class="flex flex-col gap-2 py-3">
    <div class="flex flex-wrap items-center gap-3">
      <div class="min-w-0 flex-1">
        <p class="truncate font-medium">{{ recipient }}</p>
        <p class="truncate text-sm text-muted-foreground">
          {{ roleLabel(t, invitation.role) }}<template v-if="when"> · {{ when }}</template>
        </p>
      </div>
      <Badge :variant="statusVariants[invitation.status]">{{ invitationStatusLabel(t, invitation.status) }}</Badge>
      <Button
        v-if="invitation.status === 'pending'"
        type="button"
        variant="ghost"
        tone="danger"
        :is-loading="isRevoking"
        @click="revoke"
      >
        {{ t('InvitationsPanel.revoke', 'Revoke') }}
      </Button>
    </div>
    <FailureAlert :failure="failure" />
  </li>
</template>
