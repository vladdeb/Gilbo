// analyzed_program.go
//
// Демонстрационная программа на языке Go для расчёта метрики Джилба.
//
// Программа намеренно содержит ВСЕ операторы цикла языка Go
// (единственное ключевое слово цикла — for, но у него есть четыре формы)
// и ВСЕ операторы ветвления, включая оператор множественного выбора:
//
//   Циклы:
//     1) for { ... }                     — бесконечный цикл;
//     2) for cond { ... }                — цикл с одним условием (аналог while);
//     3) for init; cond; post { ... }    — классический цикл;
//     4) for k, v := range coll { ... }  — цикл по коллекции (range).
//
//   Ветвления:
//     1) if { ... }
//     2) if { ... } else { ... }
//     3) if { ... } else if { ... } else { ... }
//     4) switch expr { case ...: ... default: ... }  (с fallthrough)
//     5) switch { case cond: ... }                   (switch без выражения)
//     6) switch v := x.(type) { case T: ... }        (type switch)
//     7) select { case ...: ... default: ... }       (множественный выбор по каналам)

package main

import (
	"errors"
	"fmt"
	"strings"
)
func processData(nums []int, limit int) (int, int) {
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

loop:
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
		if idx > 100 {
			break loop
		}
	}

	return total, count
}

func classify(value interface{}, mode int, ch <-chan int) string {
	result := ""

	switch mode {
	case 1:
		result = "low"
		fallthrough
	case 2:
		result = result + "|mid"
	case 3, 4:
		result = "high"
	default:
		result = "unknown"
	}

	switch v := value.(type) {
	case int:
		if v > 0 {
			result = fmt.Sprintf("%s:int+%d", result, v)
		} else {
			result = fmt.Sprintf("%s:int-%d", result, -v)
		}
	case string:
		for i := 0; i < len(v); i++ {
			if v[i] == 'x' {
				result += "!"
			}
		}
	case []int:
		sum := 0
		for _, item := range v {
			if item > 0 && item < 100 {
				sum += item
			}
		}
		result = fmt.Sprintf("%s:sum=%d", result, sum)
	default:
		result = result + ":other"
	}

	select {
	case x := <-ch:
		if x > 0 {
			result += "-pos"
		} else {
			result += "-neg"
		}
	default:
		result += "-empty"
	}
	return strings.ToUpper(result)
}

func validate(nums []int) error {
	for _, n := range nums {
		if n > 0 {
			if n > 1000 {
				return errors.New("too big")
			}
		} else if n < 0 {
			return errors.New("negative")
		}
	}
	return nil
}

func main() {
	ch := make(chan int, 1)
	ch <- 5

	t, c := processData([]int{1, -2, 0, 7, 120}, 50)
	fmt.Println(classify(t, 2, ch), c)

	if err := validate([]int{1, 2, 3}); err != nil {
		fmt.Println("validate error:", err)
	}
}
