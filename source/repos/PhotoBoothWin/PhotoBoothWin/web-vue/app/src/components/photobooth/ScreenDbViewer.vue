<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { callHost } from '@/composables/useHost'

defineOptions({ name: 'ScreenDbViewer' })

const emit = defineEmits<{ (e: 'close'): void }>()

interface PrintRecordRow {
  id: number
  date: string
  time: string
  isTest: boolean
  amount: number
  copies: number
  projectName?: string
  machineName?: string
  templateName?: string
}

const selectedDate = ref(formatDate(new Date()))
const rangeType = ref<'day' | 'week' | 'month'>('day')
const rows = ref<PrintRecordRow[]>([])
const totalPrintSheets = ref(0)
const totalTestSheets = ref(0)
const loading = ref(false)
const error = ref('')

function formatDate(d: Date): string {
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

async function fetchRecords() {
  loading.value = true
  error.value = ''
  try {
    const data = await callHost('get_print_records', {
      date: selectedDate.value,
      rangeType: rangeType.value,
    }) as { rows?: PrintRecordRow[]; totalPrintSheets?: number; totalTestSheets?: number }
    rows.value = Array.isArray(data.rows) ? data.rows : []
    totalPrintSheets.value = typeof data.totalPrintSheets === 'number' ? data.totalPrintSheets : 0
    totalTestSheets.value = typeof data.totalTestSheets === 'number' ? data.totalTestSheets : 0
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
    rows.value = []
    totalPrintSheets.value = 0
    totalTestSheets.value = 0
  } finally {
    loading.value = false
  }
}

const rangeLabel = computed(() => {
  switch (rangeType.value) {
    case 'week': return '周報表'
    case 'month': return '月報表'
    default: return '日報表'
  }
})

watch([selectedDate, rangeType], () => { fetchRecords() })
onMounted(() => { fetchRecords() })

function setRange(t: 'day' | 'week' | 'month') {
  rangeType.value = t
}
</script>

<template>
  <div class="screen screen--db-view" role="region" aria-label="觀看資料庫">
    <div class="db-view__toolbar">
      <h1 class="db-view__title">觀看資料庫</h1>
      <button type="button" class="btn-close" @click="emit('close')">關閉</button>
    </div>
    <div class="db-view__content">
      <aside class="db-view__sidebar">
        <label class="db-view__label">選擇日期</label>
        <input
          v-model="selectedDate"
          type="date"
          class="db-view__date-input"
          aria-label="選擇日期"
        />
        <div class="db-view__range-btns">
          <button
            type="button"
            class="range-btn"
            :class="{ active: rangeType === 'day' }"
            @click="setRange('day')"
          >
            日報表
          </button>
          <button
            type="button"
            class="range-btn"
            :class="{ active: rangeType === 'week' }"
            @click="setRange('week')"
          >
            周報表
          </button>
          <button
            type="button"
            class="range-btn"
            :class="{ active: rangeType === 'month' }"
            @click="setRange('month')"
          >
            月報表
          </button>
        </div>
        <div class="db-view__totals">
          <div class="total-row">
            <span class="total-label">列印總張數</span>
            <span class="total-value">{{ totalPrintSheets }}</span>
          </div>
          <div class="total-row total-row--test">
            <span class="total-label">測試總張數</span>
            <span class="total-value">{{ totalTestSheets }}</span>
          </div>
        </div>
      </aside>
      <main class="db-view__main">
        <p v-if="error" class="db-view__error">{{ error }}</p>
        <p v-else-if="loading" class="db-view__loading">載入中…</p>
        <div v-else class="db-view__table-scroll">
          <div class="db-view__table-wrap">
          <table class="db-view__table">
            <thead>
              <tr>
                <th>流水序號</th>
                <th>日期</th>
                <th>時間</th>
                <th>是否為測試</th>
                <th>收取金額</th>
                <th>列印數量</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="r in rows" :key="r.id">
                <td>{{ r.id }}</td>
                <td>{{ r.date }}</td>
                <td>{{ r.time }}</td>
                <td>{{ r.isTest ? '是' : '否' }}</td>
                <td>{{ r.amount }}</td>
                <td>{{ r.copies }}</td>
              </tr>
              <tr v-if="rows.length === 0">
                <td colspan="6" class="empty">此範圍無資料</td>
              </tr>
            </tbody>
          </table>
          </div>
        </div>
      </main>
    </div>
  </div>
</template>

<style lang="scss" scoped>
@use '@/styles/variables' as *;

.screen--db-view {
  position: fixed;
  inset: 0;
  z-index: 9000;
  background: #1a1a2e;
  color: #eee;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.db-view__toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 20px;
  background: #16213e;
  flex-shrink: 0;
}

.db-view__title {
  margin: 0;
  font-size: 22px;
}

.btn-close {
  padding: 8px 20px;
  font-size: 16px;
  background: #e94560;
  color: #fff;
  border: none;
  border-radius: 8px;
  cursor: pointer;
}

.db-view__content {
  display: flex;
  flex: 1;
  min-height: 0;
  height: calc(100vh - 56px);
}

.db-view__sidebar {
  width: 260px;
  padding: 20px;
  background: #0f3460;
  display: flex;
  flex-direction: column;
  gap: 16px;
  flex-shrink: 0;
}

.db-view__label {
  font-size: 14px;
  color: #aaa;
}

.db-view__date-input {
  padding: 10px 12px;
  font-size: 16px;
  border-radius: 8px;
  border: 1px solid #333;
  background: #1a1a2e;
  color: #eee;
}

.db-view__range-btns {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.range-btn {
  padding: 10px 14px;
  font-size: 15px;
  background: #1a1a2e;
  color: #ccc;
  border: 1px solid #444;
  border-radius: 8px;
  cursor: pointer;
  text-align: left;
  &.active {
    background: #e94560;
    color: #fff;
    border-color: #e94560;
  }
}

.db-view__totals {
  margin-top: 12px;
  padding-top: 12px;
  border-top: 1px solid #333;
  flex-shrink: 0;
}

.total-row {
  display: flex;
  justify-content: space-between;
  padding: 8px 0;
  font-size: 16px;
  .total-label { color: #aaa; }
  .total-value { font-weight: bold; color: #fff; }
}
.total-row--test .total-value { color: #f39c12; }

.db-view__main {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
  padding: 20px;
  overflow: hidden;
}

.db-view__table-scroll {
  flex: 1;
  min-height: 0;
  height: 0;
  overflow-y: auto;
  overflow-x: auto;
  -webkit-overflow-scrolling: touch;
}

.db-view__error {
  color: #e94560;
  margin: 0;
}
.db-view__loading {
  color: #aaa;
  margin: 0;
}

.db-view__table-wrap {
  border-radius: 8px;
  border: 1px solid #333;
}

.db-view__table {
  width: 100%;
  border-collapse: collapse;
  font-size: 14px;
  th, td {
    padding: 10px 14px;
    text-align: left;
    border-bottom: 1px solid #333;
  }
  th {
    background: #16213e;
    color: #aaa;
    font-weight: 600;
  }
  tbody tr:hover {
    background: rgba(255,255,255,0.04);
  }
  .empty {
    text-align: center;
    color: #888;
    padding: 24px;
  }
}
</style>
