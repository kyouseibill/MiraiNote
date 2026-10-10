import { unwrap, http } from '@/api/auth'

export interface RegionCountry {
  name: string
  code: string
  /** 英文短名，只参与搜索。展示用 name。 */
  englishName?: string
}

export interface RegionCity {
  name: string
  label: string
}

export const regionApi = {
  countries: () => unwrap<RegionCountry[]>(http.get('/regions/countries')),
  cities: (country: string, q: string) =>
    unwrap<RegionCity[]>(http.get('/regions/cities', { params: { country, q } })),
}
