{{- define "clearcase.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "clearcase.fullname" -}}
{{- if .Values.fullnameOverride -}}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- $name := default .Chart.Name .Values.nameOverride -}}
{{- if contains $name .Release.Name -}}
{{- .Release.Name | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}
{{- end -}}

{{- define "clearcase.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{/* Common labels; the component (api|web) is passed in as .component. */}}
{{- define "clearcase.labels" -}}
helm.sh/chart: {{ include "clearcase.chart" .root }}
app.kubernetes.io/name: {{ include "clearcase.name" .root }}
app.kubernetes.io/instance: {{ .root.Release.Name }}
app.kubernetes.io/component: {{ .component }}
app.kubernetes.io/part-of: clearcase
app.kubernetes.io/managed-by: {{ .root.Release.Service }}
app.kubernetes.io/version: {{ .root.Chart.AppVersion | quote }}
{{- with .root.Values.labels }}
{{ toYaml . }}
{{- end }}
{{- end -}}

{{- define "clearcase.selectorLabels" -}}
app.kubernetes.io/name: {{ include "clearcase.name" .root }}
app.kubernetes.io/instance: {{ .root.Release.Name }}
app.kubernetes.io/component: {{ .component }}
{{- end -}}

{{- define "clearcase.image" -}}
{{- if .image.digest -}}
{{- printf "%s@%s" .image.repository .image.digest -}}
{{- else -}}
{{- printf "%s:%s" .image.repository (default .root.Chart.AppVersion .image.tag) -}}
{{- end -}}
{{- end -}}

{{- define "clearcase.secretName" -}}
{{- default (printf "%s-accounts" (include "clearcase.fullname" .)) .Values.secrets.existingSecret -}}
{{- end -}}

{{- define "clearcase.serviceAccountName" -}}
{{- if .Values.serviceAccount.create -}}
{{- include "clearcase.fullname" . -}}
{{- else -}}
default
{{- end -}}
{{- end -}}

{{/* Fail early on combinations that would silently break or weaken the deployment. */}}
{{- define "clearcase.validate" -}}
{{- if gt (int .Values.api.replicaCount) 1 -}}
{{- fail "api.replicaCount must be 1 while the API uses SQLite (single writer on a ReadWriteOnce volume). Move to a shared database before scaling the API." -}}
{{- end -}}
{{- if and (not .Values.mock.enabled) (not .Values.ingress.tls.enabled) -}}
{{- fail "Production mode (mock.enabled=false) redirects HTTP to HTTPS and sets Secure cookies; enable ingress.tls." -}}
{{- end -}}
{{- if and .Values.kafka.enabled (not .Values.kafka.bootstrapServers) -}}
{{- fail "kafka.bootstrapServers is required when kafka.enabled=true." -}}
{{- end -}}
{{- end -}}
