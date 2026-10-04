package main

import (
	"errors"
	"fmt"
	"strings"
)

func main() {
	nums := []int{1, -2, 0, 7, 120} 
	limit := 50
	total := 0
	count := 0

	for i := 0; i < len(nums); i++ {
		n := nums[i]
		if n < 0 {
			total -= n
		} else if n == 0 {
			count++
		} else {
			if n > limit {
				total += limit
			} else {
				total += n
			}
		}
	}

	j := 0
	for j < len(nums) {
		if nums[j]%2 == 0 {
			count++
		}
		j++
	}

	k := 0
	for {
		k++
		if k > 1000 {
			break
		}
		if k%7 != 0 {
			continue
		}
		if k > 500 {
			break
		}
		count++
	}

	for idx, v := range nums {
		switch {
		case v > 100:
			total += 3
		case v > 50:
			total += 2
		case v > 10:
			total += 1
		default:
			total += 0
		}
	}

	mode := 4
	cool := 0

	switch mode {
	case 1:
		cool++
	case 2:
		cool+=5
	case 3, 4:
		if mode > 3
		{
			cool := 1000
		}
		else
		{
			for cool < 50
			{
				cool++
			}
		}
	default:
		cool := -1
	}
}