<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { regionApi, type RegionCity, type RegionCountry } from '@/api/region'
import { filterCountries, findExactCountry, formatRegion, mergeCityHits, parseRegion } from '@/utils/region'

const props = defineProps<{
  modelValue: string
  error?: string
  required?: boolean
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

const countries = ref<RegionCountry[]>([])
const country = ref('')
const countryQuery = ref('')
const countryOpen = ref(false)
const city = ref('')
const query = ref('')
const commons = ref<RegionCity[]>([])
const hits = ref<RegionCity[]>([])
const open = ref(false)
const searched = ref(false)
const searchError = ref('')
const loadError = ref('')
let searchSeq = 0

const display = computed(() => (country.value && city.value ? formatRegion(country.value, city.value) : ''))
const visibleCountries = computed(() => filterCountries(countries.value, countryQuery.value))
const countryInput = computed(() => (countryOpen.value ? countryQuery.value : country.value))

watch(
  () => props.modelValue,
  (value) => {
    const parsed = parseRegion(value)
    if (!parsed) return
    if (parsed.country === country.value && parsed.city === city.value && query.value === parsed.city) return
    country.value = parsed.country
    city.value = parsed.city
    query.value = parsed.city
    countryQuery.value = ''
    countryOpen.value = false
  },
  { immediate: true },
)

onMounted(async () => {
  try {
    countries.value = await regionApi.countries()
  } catch {
    countries.value = []
    loadError.value = '暂时无法加载国家'
  }
})

function resetCity() {
  city.value = ''
  query.value = ''
  commons.value = []
  hits.value = []
  open.value = false
  searched.value = false
  searchError.value = ''
  searchSeq += 1
}

function onCountryFocus() {
  countryQuery.value = ''
  countryOpen.value = true
}

function onCountryBlur() {
  countryOpen.value = false
  countryQuery.value = ''
}

function onCountryInput(event: Event) {
  countryQuery.value = (event.target as HTMLInputElement).value
  countryOpen.value = true
  if (!country.value && !city.value) return
  country.value = ''
  resetCity()
  emit('update:modelValue', '')
}

function onCountryKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') {
    countryOpen.value = false
    countryQuery.value = ''
    return
  }
  if (event.key !== 'Enter') return
  event.preventDefault()
  const exact = findExactCountry(visibleCountries.value, countryQuery.value)
  if (exact) selectCountry(exact)
}

function selectCountry(item: RegionCountry) {
  countryOpen.value = false
  countryQuery.value = ''
  if (item.name === country.value) return
  country.value = item.name
  resetCity()
  emit('update:modelValue', '')
  void loadCities('')
}

function onCityFocus() {
  if (!country.value || query.value.trim()) return
  if (hits.value.length > 0) {
    open.value = true
    return
  }
  void loadCities('')
}

function onCityInput(event: Event) {
  const value = (event.target as HTMLInputElement).value
  query.value = value
  searchError.value = ''
  const trimmed = value.trim()
  if (city.value && trimmed !== city.value) {
    city.value = ''
    emit('update:modelValue', '')
  }
  if (!country.value) {
    hits.value = []
    open.value = false
    searched.value = false
    searchSeq += 1
    return
  }
  void loadCities(trimmed)
}

function onCityKeydown(event: KeyboardEvent) {
  if (event.key !== 'Enter') return
  event.preventDefault()
  const trimmed = query.value.trim()
  if (!trimmed) return
  const exact = hits.value.find((hit) => hit.name === trimmed || hit.label === trimmed)
  if (exact) selectCity(exact.name)
}

function selectCity(name: string) {
  city.value = name
  query.value = name
  hits.value = []
  open.value = false
  searched.value = false
  searchError.value = ''
  emit('update:modelValue', formatRegion(country.value, name))
}

async function loadCities(text: string) {
  const seq = ++searchSeq
  const selected = country.value
  try {
    const rows = await regionApi.cities(selected, text)
    if (selected !== country.value) return
    if (!text) commons.value = rows
    if (seq !== searchSeq) return
    const merged = text ? mergeCityHits(rows, commons.value, text) : rows
    hits.value = merged
    open.value = text.length > 0 || merged.length > 0
    searched.value = true
    searchError.value = ''
  } catch {
    if (selected !== country.value || seq !== searchSeq) return
    hits.value = []
    open.value = true
    searched.value = true
    searchError.value = '暂时无法搜索城市'
  }
}
</script>

<template>
  <div data-testid="region-picker">
    <label class="mb-1 block text-sm text-gray-700" for="region-country">所在地区</label>
    <div class="grid gap-2 sm:grid-cols-2">
      <div class="relative">
        <input
          id="region-country"
          data-testid="region-country"
          type="text"
          autocomplete="off"
          spellcheck="false"
          role="combobox"
          aria-autocomplete="list"
          aria-controls="region-country-list"
          :aria-expanded="countryOpen"
          :aria-required="required || undefined"
          class="h-9 w-full rounded-md border border-gray-200 px-3 text-sm focus:outline-none focus:ring-2 focus:ring-teal-200"
          :value="countryInput"
          :placeholder="country ? country : '搜索国家'"
          @focus="onCountryFocus"
          @blur="onCountryBlur"
          @input="onCountryInput"
          @keydown="onCountryKeydown"
        />
        <ul
          v-if="countryOpen"
          id="region-country-list"
          data-testid="region-country-list"
          class="absolute z-10 mt-1 max-h-60 w-full overflow-auto rounded-md border border-gray-200 bg-white shadow"
        >
          <li v-for="item in visibleCountries" :key="item.code">
            <button
              type="button"
              class="flex w-full items-baseline gap-2 px-3 py-2 text-left text-sm hover:bg-teal-50"
              data-testid="region-country-option"
              @mousedown.prevent="selectCountry(item)"
            >
              <span data-testid="region-country-name">{{ item.name }}</span>
              <span v-if="item.englishName" class="truncate text-xs text-gray-400">{{ item.englishName }}</span>
            </button>
          </li>
          <li
            v-if="countryQuery.trim() && visibleCountries.length === 0"
            class="px-3 py-2 text-sm text-gray-500"
            data-testid="region-country-empty"
          >
            没有匹配的国家
          </li>
        </ul>
      </div>
      <div class="relative">
        <input
          id="region-city"
          data-testid="region-city"
          type="text"
          autocomplete="off"
          spellcheck="false"
          class="h-9 w-full rounded-md border border-gray-200 px-3 text-sm focus:outline-none focus:ring-2 focus:ring-teal-200 disabled:bg-gray-50 disabled:text-gray-400"
          :value="query"
          :disabled="!country"
          :placeholder="country ? '选择或搜索城市' : '请先选择国家'"
          aria-label="城市"
          @focus="onCityFocus"
          @input="onCityInput"
          @keydown="onCityKeydown"
        />
        <ul
          v-if="open"
          data-testid="region-city-list"
          class="absolute z-10 mt-1 max-h-60 w-full overflow-auto rounded-md border border-gray-200 bg-white shadow"
        >
          <li v-for="hit in hits" :key="hit.name">
            <button
              type="button"
              class="w-full px-3 py-2 text-left text-sm hover:bg-teal-50"
              data-testid="region-city-option"
              @mousedown.prevent="selectCity(hit.name)"
            >
              {{ hit.label }}
            </button>
          </li>
          <li v-if="searched && hits.length === 0 && (query.trim() || searchError)" class="px-3 py-2 text-sm text-gray-500" data-testid="region-city-empty">
            {{ searchError || '没有匹配的城市' }}
          </li>
        </ul>
      </div>
    </div>
    <p class="mt-1 text-xs text-gray-400" data-testid="region-status">
      当前：{{ display || '未填写' }}
    </p>
    <p v-if="loadError" class="mt-1 text-xs text-red-600">{{ loadError }}</p>
    <p v-if="error" class="mt-1 text-xs text-red-600" data-testid="region-error">{{ error }}</p>
  </div>
</template>
